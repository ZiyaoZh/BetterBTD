from __future__ import annotations

from io import BytesIO
import json
from pathlib import Path
import unittest
from urllib.error import HTTPError

from betterbtd_script_test.api_client import TestApiClient, TestApiClientError


TOKEN = "0123456789abcdef0123456789abcdef"


class _Response(BytesIO):
    def __init__(self, body: object, status: int = 200) -> None:
        super().__init__(json.dumps(body).encode("utf-8"))
        self.status = status


class _RawResponse(BytesIO):
    def __init__(self, body: bytes, status: int = 200) -> None:
        super().__init__(body)
        self.status = status


class _RecordingOpener:
    def __init__(self, *responses: object) -> None:
        self.responses = list(responses)
        self.calls: list[tuple[object, float]] = []

    def open(self, request: object, timeout: float) -> object:
        self.calls.append((request, timeout))
        response = self.responses.pop(0)
        if isinstance(response, BaseException):
            raise response
        return response


class TestApiClientTests(unittest.TestCase):
    def test_base_url_accepts_only_numeric_loopback_http_root(self) -> None:
        accepted = (
            ("http://127.0.0.1:18767", "http://127.0.0.1:18767/"),
            ("http://127.9.8.7/", "http://127.9.8.7/"),
            ("http://[0:0:0:0:0:0:0:1]:19001/", "http://[::1]:19001/"),
        )
        for value, normalized in accepted:
            with self.subTest(value=value):
                client = TestApiClient(value, TOKEN, opener=_RecordingOpener())
                self.assertIn(f"base_url={normalized!r}", repr(client))

        rejected = (
            "https://127.0.0.1:18767/",
            "http://localhost:18767/",
            "http://0.0.0.0:18767/",
            "http://192.168.1.20:18767/",
            "http://user@127.0.0.1:18767/",
            "http://127.0.0.1:/",
            "http://[::1]:/",
            "http://127.0.0.1:18767/api/",
            "http://127.0.0.1:18767/?debug=true",
            "http://127.0.0.1:18767/#debug",
            " http://127.0.0.1:18767/",
        )
        for value in rejected:
            with self.subTest(value=value):
                with self.assertRaises(ValueError):
                    TestApiClient(value, TOKEN)

    def test_token_and_timeout_are_validated_without_disclosure(self) -> None:
        with self.assertRaisesRegex(ValueError, "at least 32") as context:
            TestApiClient("http://127.0.0.1/", "sensitive-short-token")
        self.assertNotIn("sensitive-short-token", str(context.exception))

        for timeout in (0, -1, float("inf"), True):
            with self.subTest(timeout=timeout):
                with self.assertRaises(ValueError):
                    TestApiClient("http://127.0.0.1/", TOKEN, timeout)

    def test_health_builds_authenticated_get_request(self) -> None:
        opener = _RecordingOpener(_Response({"status": "ready"}))
        client = TestApiClient(
            "http://127.0.0.1:18767",
            TOKEN,
            timeout_seconds=2.5,
            opener=opener,
        )

        result = client.health()

        self.assertEqual({"status": "ready"}, result)
        request, timeout = opener.calls[0]
        self.assertEqual("GET", request.get_method())
        self.assertEqual("http://127.0.0.1:18767/api/test/v1/health", request.full_url)
        self.assertEqual(f"Bearer {TOKEN}", request.get_header("Authorization"))
        self.assertEqual("application/json", request.get_header("Accept"))
        self.assertIsNone(request.data)
        self.assertEqual(2.5, timeout)

    def test_write_methods_send_expected_json(self) -> None:
        opener = _RecordingOpener(*[_Response({"ok": True}) for _ in range(7)])
        client = TestApiClient("http://127.0.0.1/", TOKEN, opener=opener)

        client.start_capture(123456)
        client.validate_script(Path("E:/scripts/example.json"))
        client.execute_script({"scriptPath": "test.json", "timeoutMs": 1000})
        client.pause("test-one")
        client.resume("test-one")
        client.cancel("test-one")
        client.start_capture()

        expected = (
            ("capture/start", {"windowHandle": 123456}),
            ("scripts/validate", {"scriptPath": "E:\\scripts\\example.json"}),
            ("scripts/execute", {"scriptPath": "test.json", "timeoutMs": 1000}),
            ("operations/pause", {"operationId": "test-one"}),
            ("operations/resume", {"operationId": "test-one"}),
            ("operations/cancel", {"operationId": "test-one"}),
            ("capture/start", {}),
        )
        for (request, _), (suffix, body) in zip(opener.calls, expected, strict=True):
            with self.subTest(suffix=suffix):
                self.assertEqual("POST", request.get_method())
                self.assertTrue(request.full_url.endswith(f"/api/test/v1/{suffix}"))
                self.assertEqual("application/json", request.get_header("Content-type"))
                self.assertEqual(body, json.loads(request.data.decode("utf-8")))

    def test_get_queries_are_encoded_and_optional_status_id_is_omitted(self) -> None:
        opener = _RecordingOpener(_Response({}), _Response({}), _Response({}))
        client = TestApiClient("http://[::1]:18767/", TOKEN, opener=opener)

        client.get_status("test id/+?")
        client.get_status()
        client.get_logs("test id/+?", after_sequence=42, limit=7)

        self.assertEqual(
            "http://[::1]:18767/api/test/v1/operations/status?operationId=test+id%2F%2B%3F",
            opener.calls[0][0].full_url,
        )
        self.assertEqual(
            "http://[::1]:18767/api/test/v1/operations/status",
            opener.calls[1][0].full_url,
        )
        self.assertEqual(
            "http://[::1]:18767/api/test/v1/operations/logs"
            "?operationId=test+id%2F%2B%3F&afterSequence=42&limit=7",
            opener.calls[2][0].full_url,
        )

    def test_http_json_error_preserves_stable_fields(self) -> None:
        error = HTTPError(
            "http://127.0.0.1/api/test/v1/scripts/execute",
            409,
            "Conflict",
            {},
            _Response({"code": "busy", "message": "A controller is already active."}),
        )
        client = TestApiClient(
            "http://127.0.0.1/",
            TOKEN,
            opener=_RecordingOpener(error),
        )

        with self.assertRaises(TestApiClientError) as context:
            client.execute_script({"scriptPath": "test.json"})

        self.assertEqual("busy", context.exception.code)
        self.assertEqual("A controller is already active.", context.exception.message)
        self.assertEqual(409, context.exception.status_code)
        self.assertEqual(
            "busy: A controller is already active.",
            str(context.exception),
        )

    def test_malformed_and_non_object_json_are_rejected(self) -> None:
        responses = (
            _RawResponse(b"not json"),
            _RawResponse(b"[]"),
            _RawResponse(b'"text"'),
        )
        for response in responses:
            with self.subTest(body=response.getvalue()):
                client = TestApiClient(
                    "http://127.0.0.1/",
                    TOKEN,
                    opener=_RecordingOpener(response),
                )
                with self.assertRaises(TestApiClientError) as context:
                    client.health()
                self.assertEqual("invalidResponse", context.exception.code)

    def test_token_is_redacted_from_repr_and_all_wrapped_errors(self) -> None:
        client = TestApiClient("http://127.0.0.1/", TOKEN, opener=_RecordingOpener())
        self.assertNotIn(TOKEN, repr(client))
        self.assertEqual(
            "before <redacted> after",
            client.redact_text(f"before {TOKEN} after"),
        )

        cases = (
            _RecordingOpener(RuntimeError(f"transport exposed {TOKEN}")),
            _RecordingOpener(_RawResponse(f'{{"bad":"{TOKEN}'.encode("utf-8"))),
            _RecordingOpener(
                HTTPError(
                    "http://127.0.0.1/api/test/v1/health",
                    500,
                    "error",
                    {},
                    _Response({"code": "internalError", "message": TOKEN}),
                )
            ),
        )
        for opener in cases:
            with self.subTest(opener=type(opener.responses[0]).__name__):
                client = TestApiClient("http://127.0.0.1/", TOKEN, opener=opener)
                with self.assertRaises(TestApiClientError) as context:
                    client.health()
                self.assertNotIn(TOKEN, str(context.exception))
                self.assertNotIn(TOKEN, repr(context.exception))


if __name__ == "__main__":
    unittest.main()
