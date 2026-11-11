import importlib.util
import json
import threading
import http.client
import unittest
from pathlib import Path
from unittest.mock import patch

path = Path(__file__).with_name("monitor_api.py")
spec = importlib.util.spec_from_file_location("monitor_api", path)
api = importlib.util.module_from_spec(spec)
spec.loader.exec_module(api)


class MonitorTests(unittest.TestCase):
    def test_log_aggregates_without_identifiers(self):
        raw = ('1.2.3.4 - - [today] "GET /profile?user=secret HTTP/1.1" 200 -\n'
               '5.6.7.8 - - [today] "POST /upload HTTP/1.1" 403 -\n')
        with patch.object(api, "run", return_value=(0, raw)):
            data = api.metrics_requests("mavka-web.service")
        self.assertEqual(data["count"], 2)
        self.assertEqual(data["statuses"]["4xx"], 1)
        self.assertNotIn("profile", json.dumps(data))
        self.assertNotIn("1.2.3.4", json.dumps(data))

    def test_truncated_journal_is_not_falsely_totalled(self):
        raw = '"GET / HTTP/1.1" 200 -\n' * (api.CAP + 1)
        with patch.object(api, "run", return_value=(0, raw)):
            data = api.metrics_requests("mavka-web.service")
        self.assertFalse(data["complete"])
        self.assertIsNone(data["count"])
        self.assertEqual(data["observed"], api.CAP)

    def test_loopback_and_secret_required(self):
        with self.assertRaises(ValueError):
            api.API("0.0.0.0", 0, "x" * 40)
        with self.assertRaises(ValueError):
            api.API("127.0.0.1", 0, "x")

    def test_private_http_read_only(self):
        server = api.API("127.0.0.1", 0, "z" * 40)
        server.get_metrics = lambda: {"state": "ready", "ramPercent": 13}
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()

        def call(method, endpoint, key=None):
            c = http.client.HTTPConnection("127.0.0.1", server.server_port, timeout=3)
            headers = {"Authorization": "Bearer " + key} if key else {}
            c.request(method, endpoint, headers=headers)
            response = c.getresponse()
            status = response.status
            body = json.loads(response.read().decode())
            c.close()
            return status, body

        try:
            self.assertEqual(call("GET", "/v1/telemetry")[0], 401)
            self.assertEqual(call("GET", "/missing", "z" * 40)[0], 404)
            self.assertEqual(call("POST", "/v1/telemetry", "z" * 40)[0], 405)
            self.assertEqual(call("GET", "/v1/telemetry", "z" * 40)[1]["state"], "ready")
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=3)


if __name__ == "__main__":
    unittest.main()
