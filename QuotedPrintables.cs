using System;
using System.Collections.Generic;
using System.Text;

namespace System.Net.Imap4
{
    internal class QuotedPrintables
    {
        public static string DecodeQuotedPrintables(string InputText)
        {
            var ResultChars = new List<char>();
            Encoding encoding;
            for (int i = 0; i < InputText.Length; i++)
            {
                var CurrentChar = InputText[i];
                switch (CurrentChar)
                {
                    case '=':
                        if ((i + 1) < InputText.Length && InputText[i + 1] == '?')
                        {
                            // Encoding
                            i += 2;
                            int StIndex = InputText.IndexOf('?', i);
                            int SubStringLength = StIndex - i;
                            string encodingName = InputText.Substring(i, SubStringLength);
                            encoding = Encoding.GetEncoding(encodingName);
                            i += SubStringLength + 1;

                            //Subencoding
                            StIndex = InputText.IndexOf('?', i);
                            SubStringLength = StIndex - i;
                            string SubEncoding = InputText.Substring(i, SubStringLength);
                            i += SubStringLength + 1;

                            //Text message
                            StIndex = InputText.IndexOf("?=", i);
                            SubStringLength = StIndex - i;
                            string Message = InputText.Substring(i, SubStringLength);
                            i += SubStringLength + 1;

                            // encoding
                            switch (SubEncoding)
                            {
                                case "B":
                                    var base64EncodedBytes = Convert.FromBase64String(Message);
                                    ResultChars.AddRange(encoding.GetString(base64EncodedBytes).ToCharArray());

                                    // skip space #1
                                    if ((i + 1) < InputText.Length && InputText[i + 1] == ' ')
                                    {
                                        i++;
                                    }
                                    break;

                                case "Q":
                                    var CharByteList = new List<byte>();
                                    for (int j = 0; j < Message.Length; j++)
                                    {
                                        var QChar = Message[j];
                                        switch (QChar)
                                        {
                                            case '=':
                                                j++;
                                                string HexString = Message.Substring(j, 2);
                                                byte CharByte = Convert.ToByte(HexString, 16);
                                                CharByteList.Add(CharByte);
                                                j += 1;
                                                break;

                                            default:
                                                // Decode charbytes #1
                                                if (CharByteList.Count > 0)
                                                {
                                                    var CharString = encoding.GetString(CharByteList.ToArray());
                                                    ResultChars.AddRange(CharString.ToCharArray());
                                                    CharByteList.Clear();
                                                }

                                                ResultChars.Add(QChar);
                                                break;
                                        }
                                    }

                                    // Decode charbytes #2
                                    if (CharByteList.Count > 0)
                                    {
                                        var CharString = encoding.GetString(CharByteList.ToArray());
                                        ResultChars.AddRange(CharString.ToCharArray());
                                        CharByteList.Clear();
                                    }

                                    // skip space #2
                                    if ((i + 1) < InputText.Length && InputText[i + 1] == ' ')
                                    {
                                        i++;
                                    }
                                    break;

                                default:
                                    throw new NotSupportedException($"Decode quoted printables: unsupported subencodeing: '{SubEncoding}'");
                            }
                        }
                        else
                            ResultChars.Add(CurrentChar);
                        break;

                    default:
                        ResultChars.Add(CurrentChar);
                        break;
                }
            }

            return new string(ResultChars.ToArray());
        }
    }
}