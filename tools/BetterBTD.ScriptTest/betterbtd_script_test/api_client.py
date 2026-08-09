from __future__ import annotations

import ipaddress
import json
import math
from pathlib import Path
from typing import Any, Mapping
from urllib.error import HTTPError
from urllib.parse import urlencode, urlsplit
from urllib.request import (
    HTTPRedirectHandler,
    OpenerDirector,
    ProxyHandler,
    Request,
    build_opener,
)


_API_PREFIX = "api/test/v1"
_MINIMUM_TOKEN_LENGTH = 32


class TestApiClientError(RuntimeError):
    """Stable, credential-safe error raised by the BetterBTD Test API client."""

    def __init__(
        self,
        code: str,
        message: str,
        status_code: int | None = None,
    ) -> None:
        self.code = code
        self.message = message
        self.status_code = status_code
        super().__init__(message)

    def __str__(self) -> str:
        return f"{self.code}: {self.message}"

    def __repr__(self) -> str:
        arguments = f"code={self.code!r}, message={self.message!r}"
        if self.status_code is not None:
            arguments += f", status_code={self.status_code!r}"
        return f"{type(self).__name__}({arguments})"


class TestApiClient:
    """Small synchronous client for BetterBTD's loopback-only Test API."""

    __slots__ = ("_authorization", "_base_url", "_opener", "_timeout_seconds", "_token")

    def __init__(
        self,
        base_url: str,
        token: str,
        timeout_seconds: float = 10.0,
        *,
        opener: OpenerDirector | Any | None = None,
    ) -> None:
        self._base_url = _normalize_base_url(base_url)
        if not isinstance(token, str) or len(token) < _MINIMUM_TOKEN_LENGTH:
            raise ValueError(
                f"Test API token must be a string of at least {_MINIMUM_TOKEN_LENGTH} characters."
            )
        if (
            isinstance(timeout_seconds, bool)
            or not isinstance(timeout_seconds, (int, float))
            or not math.isfinite(timeout_seconds)
            or timeout_seconds <= 0
        ):
            raise ValueError("timeout_seconds must be a positive finite number.")

        self._token = token
        self._authorization = f"Bearer {token}"
        self._timeout_seconds = float(timeout_seconds)
        # Test API credentials must never be forwarded through an environment proxy.
        self._opener = (
            opener
            if opener is not None
            else build_opener(ProxyHandler({}), _RejectRedirectHandler())
        )

    def redact_text(self, value: str) -> str:
        """Redact this client's credential without exposing it to callers."""
        if not isinstance(value, str):
            raise TypeError("value must be a string.")
        return self._redact(value)

    def __repr__(self) -> str:
        return (
            f"{type(self).__name__}(base_url={self._base_url!r}, "
            f"token=<redacted>, timeout_seconds={self._timeout_seconds!r})"
        )

    def health(self) -> dict[str, Any]:
        return self._request("GET", "health")

    def start_capture(self, window_handle: int | None = None) -> dict[str, Any]:
        payload: dict[str, Any] = {}
        if window_handle is not None:
            payload["windowHandle"] = window_handle
        return self._request("POST", "capture/start", payload=payload)

    def validate_script(self, path: str | Path) -> dict[str, Any]:
        return self._request(
            "POST",
            "scripts/validate",
            payload={"scriptPath": str(path)},
        )

    def execute_script(self, payload: Mapping[str, Any]) -> dict[str, Any]:
        if not isinstance(payload, Mapping):
            raise TypeError("payload must be a mapping.")
        return self._request("POST", "scripts/execute", payload=dict(payload))

    def get_status(self, operation_id: str | None = None) -> dict[str, Any]:
        query = None if operation_id is None else {"operationId": operation_id}
        return self._request("GET", "operations/status", query=query)

    def get_logs(
        self,
        operation_id: str,
        after_sequence: int = 0,
        limit: int = 200,
    ) -> dict[str, Any]:
        return self._request(
            "GET",
            "operations/logs",
            query={
                "operationId": operation_id,
                "afterSequence": after_sequence,
                "limit": limit,
            },
        )

    def pause(self, operation_id: str) -> dict[str, Any]:
        return self._control("pause", operation_id)

    def resume(self, operation_id: str) -> dict[str, Any]:
        return self._control("resume", operation_id)

    def cancel(self, operation_id: str) -> dict[str, Any]:
        return self._control("cancel", operation_id)

    def _control(self, action: str, operation_id: str) -> dict[str, Any]:
        return self._request(
            "POST",
            f"operations/{action}",
            payload={"operationId": operation_id},
        )

    def _request(
        self,
        method: str,
        path: str,
        *,
        payload: Mapping[str, Any] | None = None,
        query: Mapping[str, Any] | None = None,
    ) -> dict[str, Any]:
        url = f"{self._base_url}{_API_PREFIX}/{path}"
        if query:
            url = f"{url}?{urlencode(query)}"

        body: bytes | None = None
        if payload is not None:
            try:
                body = json.dumps(
                    payload,
                    ensure_ascii=True,
                    separators=(",", ":"),
                ).encode("utf-8")
            except Exception as exception:
                raise TestApiClientError(
                    "invalidRequest",
                    self._redact(f"Request body is not JSON serializable: {exception}"),
                ) from None

        headers = {
            "Accept": "application/json",
            "Authorization": self._authorization,
        }
        if body is not None:
            headers["Content-Type"] = "application/json"
        request = Request(url, data=body, headers=headers, method=method)

        try:
            response = self._opener.open(request, timeout=self._timeout_seconds)
        except HTTPError as exception:
            self._raise_http_error(exception)
        except Exception as exception:
            raise TestApiClientError(
                "transportError",
                self._redact(f"Test API request failed: {exception}"),
            ) from None

        try:
            response_body = response.read()
            status_code = _response_status(response)
        except Exception as exception:
            raise TestApiClientError(
                "transportError",
                self._redact(f"Test API response could not be read: {exception}"),
            ) from None
        finally:
            close = getattr(response, "close", None)
            if callable(close):
                try:
                    close()
                except Exception:
                    pass

        if status_code is not None and status_code >= 400:
            self._raise_error_response(response_body, status_code)
        return self._decode_object(response_body, status_code)

    def _raise_http_error(self, exception: HTTPError) -> None:
        try:
            body = exception.read()
        except Exception:
            body = b""
        finally:
            try:
                exception.close()
            except Exception:
                pass
        self._raise_error_response(body, exception.code)

    def _raise_error_response(self, body: bytes, status_code: int) -> None:
        response = self._decode_object(body, status_code)
        code = response.get("code")
        message = response.get("message")
        if not isinstance(code, str) or not code or not isinstance(message, str) or not message:
            raise TestApiClientError(
                "invalidResponse",
                f"Test API returned HTTP {status_code} without string code and message fields.",
                status_code,
            )
        raise TestApiClientError(
            self._redact(code),
            self._redact(message),
            status_code,
        )

    def _decode_object(
        self,
        body: bytes,
        status_code: int | None,
    ) -> dict[str, Any]:
        if not isinstance(body, bytes):
            raise TestApiClientError(
                "invalidResponse",
                "Test API response body must be bytes containing a JSON object.",
                status_code,
            )
        try:
            response = json.loads(body.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as exception:
            raise TestApiClientError(
                "invalidResponse",
                self._redact(f"Test API returned invalid JSON: {exception}"),
                status_code,
            ) from None
        if not isinstance(response, dict):
            raise TestApiClientError(
                "invalidResponse",
                "Test API response must be a JSON object.",
                status_code,
            )
        return response

    def _redact(self, value: str) -> str:
        return value.replace(self._token, "<redacted>")


def _normalize_base_url(base_url: str) -> str:
    if not isinstance(base_url, str) or not base_url or base_url != base_url.strip():
        raise ValueError("base_url must be an HTTP URL for a numeric loopback address.")
    try:
        parsed = urlsplit(base_url)
        port = parsed.port
    except ValueError as exception:
        raise ValueError("base_url is invalid.") from exception

    if parsed.scheme.lower() != "http":
        raise ValueError("base_url must use HTTP.")
    if (
        not parsed.netloc
        or parsed.username is not None
        or parsed.password is not None
        or parsed.query
        or parsed.fragment
        or parsed.path not in ("", "/")
        or parsed.netloc.endswith(":")
    ):
        raise ValueError(
            "base_url must use a root path and cannot contain userinfo, query, or fragment."
        )

    hostname = parsed.hostname
    if hostname is None or "%" in hostname:
        raise ValueError("base_url must contain a numeric loopback address.")
    try:
        address = ipaddress.ip_address(hostname)
    except ValueError as exception:
        raise ValueError("base_url must contain a numeric loopback address.") from exception
    if not address.is_loopback:
        raise ValueError("base_url must contain a numeric loopback address.")

    normalized_host = f"[{address}]" if address.version == 6 else str(address)
    normalized_port = "" if port is None else f":{port}"
    return f"http://{normalized_host}{normalized_port}/"


def _response_status(response: object) -> int | None:
    status = getattr(response, "status", None)
    if status is None:
        getcode = getattr(response, "getcode", None)
        if callable(getcode):
            status = getcode()
    return status if isinstance(status, int) and not isinstance(status, bool) else None


class _RejectRedirectHandler(HTTPRedirectHandler):
    def redirect_request(
        self,
        request: Request,
        file_pointer: object,
        code: int,
        message: str,
        headers: object,
        new_url: str,
    ) -> None:
        return None
