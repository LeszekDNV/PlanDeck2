using System.Security.Cryptography;

namespace PlanDeck.Application.Planning;

public interface IShareCodeGenerator
{
    string Generate();
}

public sealed class ShareCodeGenerator : IShareCodeGenerator
{
    // Crockford base32 alphabet with the ambiguous characters (I, L, O, U) removed so codes
    // stay URL-safe and unambiguous when read aloud or copied. 10 chars ≈ 50 bits of entropy.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int Length = 10;

    public string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);
}
