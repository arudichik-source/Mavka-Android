namespace Mavka.Mobile;

/// <summary>
/// Established exchange keys make plaintext TXT/JSON inadmissible as an
/// incoming document. Key provisioning is a distinct password-sealed
/// protocol, not an exception permitting plaintext accounting imports.
/// </summary>
internal static class IncomingAttachmentSecurityPolicy
{
    public static bool RejectPlaintext(
        bool hasInstalledKeys, bool documentAuthenticated, bool keyProvisioningEnvelope) =>
        hasInstalledKeys && !documentAuthenticated && !keyProvisioningEnvelope;
}
