using Microsoft.AspNetCore.DataProtection;

namespace Trade_Bot.Services
{
    public class CredentialProtector
    {
        private readonly IDataProtector _protector;

        public CredentialProtector(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector("Binance.ApiSecret.v1");
        }

        public string Encrypt(string plain) => _protector.Protect(plain);
        public string Decrypt(string cipher) => _protector.Unprotect(cipher);
    }
}
