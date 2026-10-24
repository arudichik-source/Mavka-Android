// Production module entrypoint. Keep the externally available Relay HTTP
// handler unchanged. The named WorkerEntrypoint is accessible only through
// an account-authorized Cloudflare Service Binding, never a public URL.
import { WorkerEntrypoint } from 'cloudflare:workers';
import worker from './worker.js';
import { readDeviceTelemetry } from './device-telemetry.js';
export default worker;
export class RelayTelemetry extends WorkerEntrypoint {
  async getDeviceCounts() {
    return readDeviceTelemetry(this.env.DB);
  }
}
