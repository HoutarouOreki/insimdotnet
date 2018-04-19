﻿using System;
using System.Collections.Generic;
using System.Text;

namespace InSimDotNet {
    /// <summary>
    /// An updated method of handling unicode strings.
    /// </summary>
    public class LfsDefaultEncoding : LfsEncoding {
        private const char ControlChar = '^';
        private const string FallbackChars = "??";

        // We replace any unencoded character with two '?', none of the DBCSs can produce this as output, so it must be an error.
        private static readonly EncoderReplacementFallback EncoderFallback = new EncoderReplacementFallback(FallbackChars);

        // ExceptionFallback is only used by Mono code path, otherwise we want ReplacementFallback everywhere.
        // These codepages don't translate perfectly to LFS but the best we can do in .NET.
        private static readonly Dictionary<char, Encoding> EncodingMap = new Dictionary<char, Encoding> {
            { 'L', Encoding.GetEncoding(1252, EncoderFallback, DecoderExceptionFallback.ReplacementFallback) },
            { 'G', Encoding.GetEncoding(1253, EncoderFallback, DecoderExceptionFallback.ReplacementFallback) },
            { 'C', Encoding.GetEncoding(1251, EncoderFallback, DecoderExceptionFallback.ReplacementFallback) },
            { 'J', Encoding.GetEncoding(932, EncoderFallback, DecoderExceptionFallback.ReplacementFallback) },
            { 'E', Encoding.GetEncoding(1250, EncoderFallback, DecoderExceptionFallback.ReplacementFallback) },
            { 'T', Encoding.GetEncoding(1254, EncoderFallback, DecoderExceptionFallback.ReplacementFallback) },
            { 'B', Encoding.GetEncoding(1257, EncoderFallback, DecoderExceptionFallback.ReplacementFallback) },
            { 'H', Encoding.GetEncoding(950, EncoderFallback, DecoderExceptionFallback.ReplacementFallback) },
            { 'S', Encoding.GetEncoding(936, EncoderFallback, DecoderExceptionFallback.ReplacementFallback) },
            { 'K', Encoding.GetEncoding(949, EncoderFallback, DecoderExceptionFallback.ReplacementFallback) },
        };

        private static readonly Encoding DefaultEncoding = EncodingMap['L'];

        /// <summary>
        /// Converts a LFS encoded string to unicode.
        /// </summary>
        /// <param name="buffer">The buffer containing the packet data.</param>
        /// <param name="index">The index that the string starts in the packet data.</param>
        /// <param name="length">The length of the string.</param>
        /// <returns>The resulting unicode string.</returns>
        public override string GetString(byte[] buffer, int index, int length) {
            StringBuilder output = new StringBuilder(length);
            Encoding encoding = DefaultEncoding;
            int i = 0;
            int lastEncode = index;
            char control;
            char nextChar;

            for (i = index; i < index + length; i++) {
                control = (char)buffer[i];

                // Check for null terminator.
                if (control == Char.MinValue) {
                    break;
                }

                // Check if codepage switch needed.
                if (control == ControlChar && (i + 1) < buffer.Length) {
                    nextChar = (char)buffer[i + 1];
                    if (EncodingMap.TryGetValue(nextChar, out Encoding nextEncoding)) {
                        output.Append(encoding.GetString(buffer, lastEncode, (i - lastEncode)));
                        lastEncode = i;
                        encoding = nextEncoding;
                    }
                }
            }

            // Encode anything that's left.
            if (i - lastEncode > 0) {
                output.Append(encoding.GetString(buffer, lastEncode, (i - lastEncode)));
            }

            return output.ToString();
        }

        /// <summary>
        /// Converts a unicode string into a LFS encoded string.
        /// </summary>
        /// <param name="value">The unicode string to convert.</param>
        /// <param name="buffer">The packet buffer into which the bytes will be written.</param>
        /// <param name="index">The index in the packet buffer to start writing bytes.</param>
        /// <param name="length">The maximum number of bytes to write.</param>
        /// <returns>The number of bytes written during the operation.</returns>
        public override int GetBytes(string value, byte[] buffer, int index, int length) {
            Encoding current = DefaultEncoding;
            int start = index; // Starting position

            for (int i = 0; i < value.Length; i++) {
                char ch = value[i];

                // Switch encoding.
                if (ch == ControlChar && i < value.Length - 1) {
                    char next = value[i + 1];
                    if (EncodingMap.TryGetValue(next, out Encoding encoding)) {
                        current = encoding;
                    }
                }

                // Convert char to bytes.
                byte[] bytes = current.GetBytes(ch.ToString());

                if (ConversionFailed(bytes)) {
                    // Find a new encoding.
                    foreach (var map in EncodingMap) {
                        if (map.Value != current) {
                            bytes = map.Value.GetBytes(ControlChar + map.Key + ch.ToString());
                            if (!ConversionFailed(bytes)) {
                                current = map.Value;
                                break;
                            }
                        }
                    }
                }

                // Copy the new bytes into the buffer.
                // Final char gets dropped when nessesary to keep last byte null.
                if (index + bytes.Length < start + length) {
                    Buffer.BlockCopy(bytes, 0, buffer, index, bytes.Length);
                    index += bytes.Length;
                }
            }

            // Return number of bytes converted.
            return index - start;
        }

        private static bool ConversionFailed(byte[] bytes) {
            // Check if the encoder fallback of ?? has been used.
            return bytes.Length == 2 && bytes[0] == '?' && bytes[1] == '?' ||
                bytes.Length == 4 && bytes[2] == '?' && bytes[3] == '?';
        }
    }
}