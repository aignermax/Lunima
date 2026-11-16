"""Exercise the shared test lock with independent processes on the host OS."""
import contextlib
import errno
import io
import pathlib
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

from scripts import run_tests


WORKER = """
import sys
sys.path.insert(0, sys.argv[1])
import run_tests
run_tests.tempfile.gettempdir = lambda: sys.argv[2]
with run_tests.test_lock(timeout=float(sys.argv[3])):
    print('acquired', flush=True)
    sys.stdin.readline()
"""


class TestLockTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.temp = mock.patch.object(run_tests.tempfile, "gettempdir",
                                      return_value=self.directory.name)
        self.temp.start()
        self.addCleanup(self.temp.stop)

    def worker(self, timeout=5):
        process = subprocess.Popen(
            [sys.executable, "-u", "-c", WORKER,
             str(pathlib.Path(run_tests.__file__).parent), self.directory.name, str(timeout)],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        self.addCleanup(self.stop, process)
        return process

    @staticmethod
    def stop(process):
        if process.poll() is None:
            process.kill()
        process.communicate(timeout=10)

    def test_contender_waits_then_acquires_after_owner_releases(self):
        owner = self.worker()
        self.assertEqual("acquired\n", owner.stdout.readline())
        contender = self.worker()
        self.assertIn("Waiting for another test run", contender.stderr.readline())
        self.assertIsNone(contender.poll())
        owner.communicate(input="\n", timeout=10)
        output, errors = contender.communicate(input="\n", timeout=10)
        self.assertEqual(0, contender.returncode, errors)
        self.assertEqual("acquired\n", output)

    def test_timeout_does_not_enter_or_release_another_process_lock(self):
        owner = self.worker()
        self.assertEqual("acquired\n", owner.stdout.readline())
        with contextlib.redirect_stderr(io.StringIO()):
            for _ in range(2):
                with self.assertRaisesRegex(TimeoutError, "waiting for shared test lock"):
                    with run_tests.test_lock(timeout=0.05):
                        self.fail("Entered while another process owned the lock")
        owner.communicate(input="\n", timeout=10)
        with run_tests.test_lock(timeout=0.05):
            pass

    def test_exception_in_body_releases_lock(self):
        with self.assertRaisesRegex(RuntimeError, "body failed"):
            with run_tests.test_lock():
                raise RuntimeError("body failed")
        contender = self.worker()
        output, errors = contender.communicate(input="\n", timeout=10)
        self.assertEqual(0, contender.returncode, errors)
        self.assertEqual("acquired\n", output)

    def test_file_permission_error_is_not_retried_as_contention(self):
        with mock.patch.object(pathlib.Path, "open", side_effect=PermissionError(errno.EACCES, "denied")):
            with self.assertRaises(PermissionError):
                with run_tests.test_lock():
                    self.fail("Entered without opening lock file")

    def test_lock_timeout_must_be_positive(self):
        with contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit) as error:
                run_tests.main(["--lock-timeout", "0", "--dry-run"])
        self.assertEqual(2, error.exception.code)


if __name__ == "__main__":
    unittest.main()
