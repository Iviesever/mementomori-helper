"""Synthetic parser regressions: no Android device, network, account or key needed."""
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('android_smoke', Path(__file__).with_name('android-smoke.py'))
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)
PREFIX = 'Verifies\nVerified using v2 scheme (APK Signature Scheme v2): true\nNumber of signers: 1\n'
CERT = 'ab' * 32

class UpdateEvidenceTests(unittest.TestCase):
    def test_installed_version(self):
        self.assertEqual(1002, smoke.version_code('  versionCode=1002 minSdk=24 targetSdk=36'))
    def test_repeated_same_version(self):
        self.assertEqual(1001, smoke.version_code('versionCode=1001\nversionCode=1001'))
    def test_missing_version(self):
        with self.assertRaises(RuntimeError): smoke.version_code('')
    def test_ambiguous_version(self):
        with self.assertRaises(RuntimeError): smoke.version_code('versionCode=1001\nversionCode=1002')
    def test_zero_version(self):
        with self.assertRaises(RuntimeError): smoke.version_code('versionCode=0')
    def test_classic_signer(self):
        self.assertEqual(CERT, smoke.signing_identity(PREFIX + f'Signer #1 certificate SHA-256 digest: {CERT}\n'))
    def test_sdk36_signer(self):
        self.assertEqual(CERT, smoke.signing_identity(PREFIX + f'V3.0 Signer: certificate SHA-256 digest: {CERT.upper()}\r\n'))
    def test_unverified(self):
        with self.assertRaises(RuntimeError): smoke.signing_identity(f'Signer #1 certificate SHA-256 digest: {CERT}')
    def test_source_stamp_is_not_identity(self):
        with self.assertRaises(RuntimeError): smoke.signing_identity(PREFIX + f'Source Stamp Signer certificate SHA-256 digest: {CERT}')
    def test_public_key_is_not_identity(self):
        with self.assertRaises(RuntimeError): smoke.signing_identity(PREFIX + f'Signer #1 public key SHA-256 digest: {CERT}')
    def test_two_certificates_rejected(self):
        with self.assertRaises(RuntimeError): smoke.signing_identity(PREFIX + f'Signer #1 certificate SHA-256 digest: {CERT}\nSigner #2 certificate SHA-256 digest: {"cd"*32}\n')
    def test_failed_v2_rejected(self):
        with self.assertRaises(RuntimeError): smoke.signing_identity(PREFIX.replace('true', 'false') + f'Signer #1 certificate SHA-256 digest: {CERT}')

if __name__ == '__main__':
    unittest.main()
