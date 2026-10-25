// Production module entrypoint. Keep the externally available Relay HTTP
// handler unchanged. The named WorkerEntrypoint is accessible only through
// an account-authorized Cloudflare Service Binding, never a public URL.
import { WorkerEntrypoint } from 'cloudflare:workers';
import worker from './worker.js';
import { readDeviceTelemetry } from './device-telemetry.js';
import { readSecurityRollups } from './security-rollups.js';
import { readMaintenanceCheckpoint } from './maintenance.js';
export default worker;
export class RelayTelemetry extends WorkerEntrypoint {
  async getDeviceCounts() {
    return readDeviceTelemetry(this.env.DB);
  }
  async getSecurityOverview() {
    // Same named private RPC as device counts; aggregate observations only.
    return readSecurityRollups(this.env.DB);
  }
  async getMaintenanceStatus() {
    // Fixed one-row D1 health marker. No message or device table scans.
    return readMaintenanceCheckpoint(this.env.DB);
  }
}
