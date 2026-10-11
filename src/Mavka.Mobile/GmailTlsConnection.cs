using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace Mavka.Mobile;

/// <summary>
/// Preserve normal certificate/name/chain validation. Retry only if the
/// first failure consists exclusively of unavailable revocation information.
/// Do NOT accept expired, self-signed, untrusted, or wrong-host certificates.
/// </summary>
internal static class GmailTlsConnection
{
    private static bool OnlyUnknownRevocation(
        SslPolicyErrors errors, X509Chain? chain)
    {
        if (errors != SslPolicyErrors.RemoteCertificateChainErrors ||
            chain is null || chain.ChainStatus.Length == 0)
            return false;

        const X509ChainStatusFlags allowed =
            X509ChainStatusFlags.RevocationStatusUnknown |
            X509ChainStatusFlags.OfflineRevocation;

        return chain.ChainStatus.All(
            item => item.Status != X509ChainStatusFlags.NoError &&
                    (item.Status & ~allowed) == 0);
    }

    public static async Task<(ImapClient Client, bool RevocationFallback)> OpenImapAsync(
        CancellationToken cancellationToken)
    {
        bool onlyUnknownRevocation = false;
        var original = new ImapClient();
        original.ServerCertificateValidationCallback = (_, _, chain, errors) =>
        {
            onlyUnknownRevocation = OnlyUnknownRevocation(errors, chain);
            return errors == SslPolicyErrors.None;
        };

        try
        {
            await original.ConnectAsync("imap.gmail.com", 993,
                SecureSocketOptions.SslOnConnect, cancellationToken);
            return (original, false);
        }
        catch (SslHandshakeException) when (onlyUnknownRevocation)
        {
            original.Dispose();

            // MailKit still checks the CA chain, hostname, and validity dates.
            // This retry omits ONLY the unavailable CRL/OCSP revocation lookup.
            var retry = new ImapClient { CheckCertificateRevocation = false };
            try
            {
                await retry.ConnectAsync("imap.gmail.com", 993,
                    SecureSocketOptions.SslOnConnect, cancellationToken);
                return (retry, true);
            }
            catch
            {
                retry.Dispose();
                throw;
            }
        }
        catch
        {
            original.Dispose();
            throw;
        }
    }

    public static async Task<(SmtpClient Client, bool RevocationFallback)> OpenSmtpAsync(
        CancellationToken cancellationToken)
    {
        bool onlyUnknownRevocation = false;
        var original = new SmtpClient();
        original.ServerCertificateValidationCallback = (_, _, chain, errors) =>
        {
            onlyUnknownRevocation = OnlyUnknownRevocation(errors, chain);
            return errors == SslPolicyErrors.None;
        };

        try
        {
            await original.ConnectAsync("smtp.gmail.com", 465,
                SecureSocketOptions.SslOnConnect, cancellationToken);
            return (original, false);
        }
        catch (SslHandshakeException) when (onlyUnknownRevocation)
        {
            original.Dispose();

            var retry = new SmtpClient { CheckCertificateRevocation = false };
            try
            {
                await retry.ConnectAsync("smtp.gmail.com", 465,
                    SecureSocketOptions.SslOnConnect, cancellationToken);
                return (retry, true);
            }
            catch
            {
                retry.Dispose();
                throw;
            }
        }
        catch
        {
            original.Dispose();
            throw;
        }
    }
}
