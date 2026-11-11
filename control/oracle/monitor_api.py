"""Private read-only Oracle VM monitoring (standard library, no credentials in code)."""
import datetime
import hmac
import json
import os
import re
import shutil
import subprocess
import time
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

UNITS=("mavka-web.service","mavka-sync-api.service","caddy.service",
       "cloudflared.service","mavka-auth-v2-1.service","mavka-panel-web.service")
LOG_RE=re.compile(r'"(?:GET|POST|HEAD|PUT|PATCH|DELETE|OPTIONS) [^"\n]{1,3000} HTTP/\d\.\d"\s+(\d{3})(?:\s|$)')
CAP=10000

def run(args,timeout=5):
    try:
        x=subprocess.run(args,capture_output=True,text=True,timeout=timeout,check=False)
        return x.returncode,x.stdout
    except (OSError,subprocess.TimeoutExpired):
        return -1,""

def metric_cpu():
    def sample():
        with open("/proc/stat",encoding="ascii") as f:
            v=list(map(int,f.readline().split()[1:]))
        return sum(v),v[3]+v[4]
    try:
        a=sample();time.sleep(.12);b=sample();t=b[0]-a[0]
        return round(max(0,min(100,100*(1-(b[1]-a[1])/t))),1) if t>0 else None
    except (OSError,ValueError,IndexError,ZeroDivisionError):
        return None

def metric_ram():
    try:
        with open("/proc/meminfo",encoding="ascii") as f: s=f.read()
        t=int(re.search(r"^MemTotal:\s+(\d+)",s,re.M).group(1))
        a=int(re.search(r"^MemAvailable:\s+(\d+)",s,re.M).group(1))
        return round(100*(t-a)/t,1) if t else None
    except (OSError,ValueError,AttributeError):
        return None

def metrics_requests(unit):
    day=datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%d")
    rc,raw=run(["journalctl","--quiet","--no-pager","--output=cat","-u",unit,
                "--since",day+" 00:00:00 UTC","-n",str(CAP+1)])
    if rc:
        return {"day":day,"complete":False,"count":None,"observed":None}
    lines=raw.splitlines();complete=len(lines)<=CAP
    groups={"2xx":0,"3xx":0,"4xx":0,"5xx":0}
    for line in lines[-CAP:]:
        m=LOG_RE.search(line)
        if m and m.group(1)[0] in "2345":
            groups[m.group(1)[0]+"xx"]+=1
    count=sum(groups.values())
    return {"day":day,"complete":complete,"count":count if complete else None,
            "observed":count,"statuses":groups if complete else None}

def service_state(unit):
    rc,out=run(["systemctl","is-active","--",unit],2)
    return "active" if rc==0 and out.strip()=="active" else "inactive" if rc==3 else "unknown"

def probe():
    try:
        with urllib.request.urlopen("http://127.0.0.1:18769/",timeout=1.5) as r:
            return "online" if r.status==200 else "degraded"
    except Exception:
        return "offline"

def collect():
    try: disk=round(shutil.disk_usage("/").free/1e9,2)
    except OSError: disk=None
    return {"state":"ready","checkedAt":datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds").replace("+00:00","Z"),
            "cpuPercent":metric_cpu(),"ramPercent":metric_ram(),"diskFreeGb":disk,
            "siteProbe":probe(),"siteRequests":metrics_requests("mavka-web.service"),
            "apiRequests":metrics_requests("mavka-sync-api.service"),
            "services":{x:service_state(x) for x in UNITS}}

class API(ThreadingHTTPServer):
    daemon_threads=True
    def __init__(self,host,port,key):
        if host!="127.0.0.1" or not isinstance(key,str) or len(key)<32:
            raise ValueError("local bind and strong key required")
        self.key=key;self.last=None;self.last_at=0
        super().__init__((host,port),Handler)
    def get_metrics(self):
        if self.last is not None and time.monotonic()-self.last_at<60:return self.last
        self.last=collect();self.last_at=time.monotonic()
        return self.last

class Handler(BaseHTTPRequestHandler):
    server_version="OracleMonitor"
    sys_version=""
    def log_message(self,*args):pass
    def send(self,status,data):
        content=json.dumps(data,separators=(",",":")).encode()
        self.send_response(status);self.send_header("Content-Type","application/json")
        self.send_header("Content-Length",str(len(content)))
        self.send_header("Cache-Control","no-store");self.end_headers();self.wfile.write(content)
    def do_GET(self):
        supplied=self.headers.get("Authorization","")
        if len(supplied)>512 or not hmac.compare_digest(supplied,"Bearer "+self.server.key):
            return self.send(401,{"error":"unauthorized"})
        if self.path!="/v1/telemetry":return self.send(404,{"error":"not_found"})
        try:return self.send(200,self.server.get_metrics())
        except Exception:return self.send(503,{"error":"telemetry_unavailable"})
    def do_POST(self):self.send(405,{"error":"method_not_allowed"})

if __name__=="__main__":
    API("127.0.0.1",7340,os.environ.get("ORACLE_MONITOR_KEY")).serve_forever()
