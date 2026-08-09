from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

from betterbtd_script_test.game_driver_client import (
    GameDriverClient,
    GameDriverClientError,
    next_observation_index,
)


REPOSITORY_ROOT = Path(__file__).resolve().parents[3]


class GameDriverClientTests(unittest.TestCase):
    def test_catalog_parses_one_json_object(self) -> None:
        runner = _FakeRunner([{"catalog": "ok"}])
        client = GameDriverClient(REPOSITORY_ROOT, process_runner=runner)

        result = client.validate_catalog()

        self.assertEqual({"catalog": "ok"}, result)
        self.assertIn("catalog", runner.calls[0][0])

    def test_game_driver_environment_does_not_inherit_credentials(self) -> None:
        runner = _FakeRunner([{"catalog": "ok"}])
        custom_variable_secret = "secret-from-arbitrary-environment-name"
        client = GameDriverClient(
            REPOSITORY_ROOT,
            process_runner=runner,
            secrets_to_scrub=(custom_variable_secret,),
        )
        previous = os.environ.get("BETTERBTD_TEST_API_TOKEN")
        previous_custom = os.environ.get("ORDINARY_NAME")
        previous_safe = os.environ.get("SAFE_VALUE")
        os.environ["BETTERBTD_TEST_API_TOKEN"] = "do-not-copy"
        os.environ["ORDINARY_NAME"] = custom_variable_secret
        os.environ["SAFE_VALUE"] = "preserved"
        try:
            client.validate_catalog()
        finally:
            if previous is None:
                os.environ.pop("BETTERBTD_TEST_API_TOKEN", None)
            else:
                os.environ["BETTERBTD_TEST_API_TOKEN"] = previous
            if previous_custom is None:
                os.environ.pop("ORDINARY_NAME", None)
            else:
                os.environ["ORDINARY_NAME"] = previous_custom
            if previous_safe is None:
                os.environ.pop("SAFE_VALUE", None)
            else:
                os.environ["SAFE_VALUE"] = previous_safe

        environment = runner.calls[0][1]["env"]
        self.assertNotIn("BETTERBTD_TEST_API_TOKEN", environment)
        self.assertNotIn("ORDINARY_NAME", environment)
        self.assertEqual("preserved", environment["SAFE_VALUE"])

    def test_act_and_assert_cannot_activate_the_window(self) -> None:
        client = GameDriverClient(REPOSITORY_ROOT, process_runner=_FakeRunner([]))
        for phase in ("act", "assert"):
            with self.subTest(phase=phase):
                with self.assertRaisesRegex(GameDriverClientError, "forbidden"):
                    client.observe(
                        _FakeContext(Path("unused")),
                        phase=phase,
                        index=0,
                        activate=True,
                    )

    def test_nonzero_driver_exit_is_rejected(self) -> None:
        runner = _FakeRunner([], returncode=3, stderr='{"error":{"code":"bad"}}')
        client = GameDriverClient(REPOSITORY_ROOT, process_runner=runner)

        with self.assertRaisesRegex(GameDriverClientError, "exit code 3"):
            client.validate_catalog()

    def test_observation_passes_png_output_then_recognizes_adjacent_metadata(self) -> None:
        runner = _FakeRunner(
            [
                {"window": {"handle": "0x1234"}},
                {"recognition": {"status": "matched", "oracleEligible": True}},
            ]
        )
        client = GameDriverClient(REPOSITORY_ROOT, process_runner=runner)
        with tempfile.TemporaryDirectory() as temporary_directory:
            context = _FakeContext(Path(temporary_directory))
            observation = client.observe(context, phase="act", index=0)

        capture_command = runner.calls[0][0]
        recognize_command = runner.calls[1][0]
        output_path = Path(capture_command[capture_command.index("--output") + 1])
        evidence_path = Path(recognize_command[recognize_command.index("--evidence") + 1])
        self.assertEqual(".png", output_path.suffix)
        self.assertEqual(".json", evidence_path.suffix)
        self.assertEqual(observation.evidence_path, evidence_path)

    def test_next_observation_index_allows_recover_verification_retry(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            context = _FakeContext(Path(temporary_directory))
            first = context.artifact_directory / "observations" / "recover" / "000000"
            first.mkdir(parents=True)

            self.assertEqual(1, next_observation_index(context, "recover"))


class _FakeContext:
    def __init__(self, artifact_directory: Path) -> None:
        self.artifact_directory = artifact_directory


class _FakeRunner:
    def __init__(
        self,
        responses: list[dict[str, object]],
        *,
        returncode: int = 0,
        stderr: str = "",
    ) -> None:
        self._responses = list(responses)
        self._returncode = returncode
        self._stderr = stderr
        self.calls: list[tuple[list[str], dict[str, object]]] = []

    def __call__(self, command, **kwargs):
        self.calls.append((list(command), kwargs))
        response = self._responses.pop(0) if self._responses else {}
        return subprocess.CompletedProcess(
            command,
            self._returncode,
            stdout=json.dumps(response),
            stderr=self._stderr,
        )


if __name__ == "__main__":
    unittest.main()
