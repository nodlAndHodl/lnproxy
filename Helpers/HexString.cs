using Google.Protobuf;

namespace LnProxyApi.Helpers
{
    public class HexStringHelper
    {

        private static bool IsValidHexString(string hexString)
        {
            // Check if the string is null or has an odd length (invalid hex string).
            if (hexString == null || hexString.Length % 2 != 0)
            {
                return false;
            }

            // Check if each character in the string is a valid hexadecimal digit.
            foreach (char c in hexString)
            {
                if (!IsHexDigit(c))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        public static ByteString HexStringToByteString(string hexString)
        {
            if (!IsValidHexString(hexString))
            {
                throw new ArgumentException("Invalid hex string");
            }
            
            int length = hexString.Length;
            byte[] bytes = new byte[length / 2];

            for (int i = 0; i < length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hexString.Substring(i, 2), 16);
            }

            return ByteString.CopyFrom(bytes);
        }
    }
    
}
