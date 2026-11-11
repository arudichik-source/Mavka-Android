# Oracle VM / MaVka WEB monitoring

Read-only, loopback-only API on 127.0.0.1:7340, independent from existing SSH/VNC, site, Mavka Relay and Shorts Studio. It reports CPU, RAM, disk, site probe, named service state and aggregate HTTP request counts from systemd journals for UTC day. Counts are **HTTP requests, not unique visitors**; source is the local origin log and may undercount CDN-cached traffic. No raw logs, paths, IP addresses, keys or user details leave the VM. If more than 10000 journal lines, daily count is null and sample is marked incomplete.

Requires distinct Cloudflare VPC Service limited to HTTP 127.0.0.1:7340 using existing Tunnel, private binding ORACLE_MONITOR_VPC and Worker secret ORACLE_MONITOR_KEY matching locally generated /home/ubuntu/oracle-monitor/private.env. No public route and no shared Shorts secret. No new D1, R2, Cron or public website endpoints. Dashboard keeps 15-minute refresh only while browser is open. Do not modify remote access.
