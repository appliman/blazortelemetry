using System.Security.Cryptography;
using System.Text;

namespace Blazor2fa;

public class TwoFactorAuthenticator
{
	public static DateTime _epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
	public TimeSpan DefaultClockDriftTolerance { get; set; }
	public bool UseManagedSha1Algorithm { get; set; }
	public bool TryUnmanagedAlgorithmOnFailure { get; set; }

	public TwoFactorAuthenticator() : this(true, true) { }

	public TwoFactorAuthenticator(bool useManagedSha1, bool useUnmanagedOnFail)
	{
		DefaultClockDriftTolerance = TimeSpan.FromMinutes(5);
		UseManagedSha1Algorithm = useManagedSha1;
		TryUnmanagedAlgorithmOnFailure = useUnmanagedOnFail;
	}

	/// <summary>
	/// Generate a setup code for a Google Authenticator user to scan.
	/// </summary>
	/// <param name="applicationName">Application name displayed by the authenticator.</param>
	/// <param name="accountSecretKey">Account Secret Key</param>
	/// <param name="qrCodeWidth">QR Code Width</param>
	/// <param name="qrCodeHeight">QR Code Height</param>
	/// <returns>SetupCode object</returns>
	internal SetupCode GenerateSetupCode(string applicationName, string accountSecretKey, int qrCodeWidth, int qrCodeHeight)
	{
		return GenerateSetupCode(null, applicationName, accountSecretKey, qrCodeWidth, qrCodeHeight);
	}

	/// <summary>
	/// Generate a setup code for a Google Authenticator user to scan (with issuer ID).
	/// </summary>
	/// <param name="issuer">Issuer ID (the name of the system, i.e. 'MyApp')</param>
	/// <param name="applicationName">Application name displayed by the authenticator.</param>
	/// <param name="accountSecretKey">Account Secret Key</param>
	/// <param name="qrCodeWidth">QR Code Width</param>
	/// <param name="qrCodeHeight">QR Code Height</param>
	/// <returns>SetupCode object</returns>
	internal SetupCode GenerateSetupCode(string? issuer, string applicationName, string accountSecretKey, int qrCodeWidth, int qrCodeHeight)
	{
		return GenerateSetupCode(issuer, applicationName, accountSecretKey, qrCodeWidth, qrCodeHeight, false);
	}

	/// <summary>
	/// Generate a setup code for a Google Authenticator user to scan (with issuer ID).
	/// </summary>
	/// <param name="issuer">Issuer ID (the name of the system, i.e. 'MyApp')</param>
	/// <param name="applicationName">Application name displayed by the authenticator.</param>
	/// <param name="accountSecretKey">Account Secret Key</param>
	/// <param name="qrCodeWidth">QR Code Width</param>
	/// <param name="qrCodeHeight">QR Code Height</param>
	/// <param name="useHttps">Use HTTPS instead of HTTP</param>
	/// <returns>SetupCode object</returns>
	internal SetupCode GenerateSetupCode(string? issuer, string applicationName, string accountSecretKey, int qrCodeWidth, int qrCodeHeight, bool useHttps)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

		var normalizedApplicationName = applicationName.Trim();
		var normalizedIssuer = issuer?.Trim();
		var accountLabel = string.IsNullOrWhiteSpace(normalizedIssuer)
			? normalizedApplicationName
			: $"{normalizedIssuer}:{normalizedApplicationName}";
		var encodedSecretKey = EncodeAccountSecretKey(accountSecretKey);
		var query = $"secret={Uri.EscapeDataString(encodedSecretKey)}";
		if (!string.IsNullOrWhiteSpace(normalizedIssuer))
		{
			query += $"&issuer={Uri.EscapeDataString(normalizedIssuer)}";
		}

		var provisioningUri = $"otpauth://totp/{Uri.EscapeDataString(accountLabel)}?{query}";
		var protocol = useHttps ? "https" : "http";
		var url = $"{protocol}://chart.googleapis.com/chart?cht=qr&chs={qrCodeWidth}x{qrCodeHeight}&chl={Uri.EscapeDataString(provisioningUri)}";

		return new SetupCode
		{
			Account = normalizedApplicationName,
			AccountSecretKey = accountSecretKey,
			ManualEntryKey = encodedSecretKey,
			QrCodeSetupImageUrl = url
		};
	}

	private string EncodeAccountSecretKey(string accountSecretKey)
	{
		//if (accountSecretKey.Length < 10)
		//{
		//    accountSecretKey = accountSecretKey.PadRight(10, '0');
		//}

		//if (accountSecretKey.Length > 12)
		//{
		//    accountSecretKey = accountSecretKey.Substring(0, 12);
		//}

		return Base32Encode(Encoding.UTF8.GetBytes(accountSecretKey));
	}

	private string Base32Encode(byte[] data)
	{
		int inByteSize = 8;
		int outByteSize = 5;
		char[] alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".ToCharArray();

		int i = 0, index = 0, digit = 0;
		int current_byte, next_byte;
		StringBuilder result = new StringBuilder((data.Length + 7) * inByteSize / outByteSize);

		while (i < data.Length)
		{
			current_byte = (data[i] >= 0) ? data[i] : (data[i] + 256); // Unsign

			/* Is the current digit going to span a byte boundary? */
			if (index > (inByteSize - outByteSize))
			{
				if ((i + 1) < data.Length)
					next_byte = (data[i + 1] >= 0) ? data[i + 1] : (data[i + 1] + 256);
				else
					next_byte = 0;

				digit = current_byte & (0xFF >> index);
				index = (index + outByteSize) % inByteSize;
				digit <<= index;
				digit |= next_byte >> (inByteSize - index);
				i++;
			}
			else
			{
				digit = (current_byte >> (inByteSize - (index + outByteSize))) & 0x1F;
				index = (index + outByteSize) % inByteSize;
				if (index == 0)
					i++;
			}
			result.Append(alphabet[digit]);
		}

		return result.ToString();
	}

	public string GeneratePINAtInterval(string accountSecretKey, long counter, int digits = 6)
	{
		return GenerateHashedCode(accountSecretKey, counter, digits);
	}

	internal string GenerateHashedCode(string secret, long iterationNumber, int digits = 6)
	{
		byte[] key = Encoding.UTF8.GetBytes(secret);
		return GenerateHashedCode(key, iterationNumber, digits);
	}

	internal string GenerateHashedCode(byte[] key, long iterationNumber, int digits = 6)
	{
		byte[] counter = BitConverter.GetBytes(iterationNumber);

		if (BitConverter.IsLittleEndian)
		{
			Array.Reverse(counter);
		}

		HMACSHA1 hmac = getHMACSha1Algorithm(key);

		byte[] hash = hmac.ComputeHash(counter);

		int offset = hash[hash.Length - 1] & 0xf;

		// Convert the 4 bytes into an integer, ignoring the sign.
		int binary =
			((hash[offset] & 0x7f) << 24)
			| (hash[offset + 1] << 16)
			| (hash[offset + 2] << 8)
			| (hash[offset + 3]);

		int password = binary % (int)Math.Pow(10, digits);
		return password.ToString(new string('0', digits));
	}

	private long GetCurrentCounter()
	{
		return GetCurrentCounter(DateTime.UtcNow, _epoch, 30);
	}

	private long GetCurrentCounter(DateTime now, DateTime epoch, int timeStep)
	{
		return (long)(now - epoch).TotalSeconds / timeStep;
	}

	/// <summary>
	/// Creates the platform HMACSHA1 implementation used to hash counter bytes.
	/// </summary>
	/// <param name="key">User's secret key, in bytes</param>
	/// <returns>HMACSHA1 cryptographic algorithm</returns>
	private HMACSHA1 getHMACSha1Algorithm(byte[] key)
	{
		return new HMACSHA1(key);
	}

	public bool ValidateTwoFactorPIN(string accountSecretKey, string twoFactorCodeFromClient)
	{
		return ValidateTwoFactorPIN(accountSecretKey, twoFactorCodeFromClient, DefaultClockDriftTolerance);
	}

	public bool ValidateTwoFactorPIN(string accountSecretKey, string twoFactorCodeFromClient, TimeSpan timeTolerance)
	{
		var codes = GetCurrentPINs(accountSecretKey, timeTolerance);
		return codes.Any(c => c == twoFactorCodeFromClient);
	}

	public string GetCurrentPIN(string accountSecretKey)
	{
		return GeneratePINAtInterval(accountSecretKey, GetCurrentCounter());
	}

	public string GetCurrentPIN(string accountSecretKey, DateTime now)
	{
		return GeneratePINAtInterval(accountSecretKey, GetCurrentCounter(now, _epoch, 30));
	}

	public string[] GetCurrentPINs(string accountSecretKey)
	{
		return GetCurrentPINs(accountSecretKey, DefaultClockDriftTolerance);
	}

	public string[] GetCurrentPINs(string accountSecretKey, TimeSpan timeTolerance)
	{
		List<string> codes = new List<string>();
		long iterationCounter = GetCurrentCounter();
		int iterationOffset = 0;

		if (timeTolerance.TotalSeconds > 30)
		{
			iterationOffset = Convert.ToInt32(timeTolerance.TotalSeconds / 30.00);
		}

		long iterationStart = iterationCounter - iterationOffset;
		long iterationEnd = iterationCounter + iterationOffset;

		for (long counter = iterationStart; counter <= iterationEnd; counter++)
		{
			codes.Add(GeneratePINAtInterval(accountSecretKey, counter));
		}

		return codes.ToArray();
	}

}
