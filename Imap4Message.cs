/*
* Copyright (c) 2011, Darren Horrocks
* All rights reserved.
* 
* Redistribution and use in source and binary forms, with or without modification, 
* are permitted provided that the following conditions are met:
* 
* Redistributions of source code must retain the above copyright notice, this list 
* of conditions and the following disclaimer.
* Redistributions in binary form must reproduce the above copyright notice, this 
* list of conditions and the following disclaimer in the documentation and/or 
* other materials provided with the distribution.
* Neither the name of Darren Horrocks/www.bizzeh.com nor the names of its 
* contributors may be used to endorse or promote products derived from this software 
* without specific prior written permission.
* 
* THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY 
* EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES 
* OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT 
* SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, 
* INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
* PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
* INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, 
* STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF 
* THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/
using System.Collections.Generic;
using System.Text;

namespace System.Net.Imap4
{
	/// <summary>
	/// Digested Imap4 mail message
	/// </summary>
	public class Imap4Message
	{
		/// <summary>
		/// 
		/// </summary>
		public List<Imap4Attachment> Attachments { get; private set; } = new List<Imap4Attachment>();
		/// <summary>
		/// 
		/// </summary>
		public Imap4HeaderList Headers { get; private set; } = new Imap4HeaderList();
		/// <summary>
		/// 
		/// </summary>
		public String Body => string.IsNullOrWhiteSpace(BodyHtml) ? BodyText : BodyHtml;

		/// <summary>
		/// 
		/// </summary>
		public String BodyText { get; private set; } = "";
		/// <summary>
		/// 
		/// </summary>
		public String BodyHtml { get; private set; } = "";
		/// <summary>
		/// 
		/// </summary>
		public String Subject { get; private set; } = "";
		/// <summary>
		/// 
		/// </summary>
		public String SubjectDecoded => QuotedPrintables.DecodeQuotedPrintables(Subject);

		/// <summary>
		/// 
		/// </summary>
		public String From { get; private set; } = "";
		/// <summary>
		/// 
		/// </summary>
		public String To { get; private set; } = "";
		/// <summary>
		/// 
		/// </summary>
		public String Cc { get; private set; } = "";
		/// <summary>
		/// 
		/// </summary>
		public String Bcc { get; private set; } = "";
		/// <summary>
		/// 
		/// </summary>
		public String ReplyTo { get; private set; } = "";
		/// <summary>
		/// 
		/// </summary>
		public double MimeVersion { get; private set; } = 0;
		/// <summary>
		/// 
		/// </summary>
		public String ContentType { get; private set; } = "text/plain";
		/// <summary>
		/// 
		/// </summary>
		public String ContentBoundary { get; private set; } = "";
		/// <summary>
		/// 
		/// </summary>
		public DateTime Date { get; private set; } = DateTime.Now;
		/// <summary>
		/// 
		/// </summary>
		public bool IsReply { get; private set; } = false;
		/// <summary>
		/// 
		/// </summary>
		public bool IsReceipt { get; private set; } = false;
		/// <summary>
		/// 
		/// </summary>
		public String Raw { get; private set; }

		/// <summary>
		/// 
		/// </summary>
		/// <param name="mimetype"></param>
		/// <param name="lines"></param>
		/// <param name="start"></param>
		/// <param name="msg"></param>
		public delegate bool MimeTypeHandlerCB(String mimetype, String[] lines, ref int start, Imap4Message msg);
		/// <summary>
		/// 
		/// </summary>
		static public MimeTypeHandlerCB MimeTypeHandler;


		public Imap4Message()
		{
		}

		private void ParseMessageSection(String bound, String[] lines, ref int i)
		{
			for (; i < lines.Length; i++)
			{
				if (lines[i] == "--" + bound + "--") return; //its all over for this section

				if (lines[i] != "--" + bound) continue;

				//beginning of the section, start looking for headers
				String currentType = "text/plain";
				String newBound = "";
				String filename = "";
				String transportEncoding = "plain";
				bool isAttachment = false;

				while (lines[i] != "")
				{
					if (lines[i].StartsWith("Content-Type"))
					{
						String[] parts = lines[i].Split(':');
						String[] bits = parts[1].Split(';');
						currentType = bits[0].Trim();

						for (int x = 0; x < bits.Length; x++)
						{
							bits[x] = bits[x].Trim();
							if (bits[x].StartsWith("boundary=\""))
							{
								newBound = bits[x].Substring(10, bits[x].Length - 11);
							}
							else if (bits[x].StartsWith("boundary"))
							{
								newBound = bits[x].Substring(9, bits[x].Length - 9);
							}
						}
					}
					else if (lines[i].StartsWith("Content-Disposition"))
					{
						String[] parts = lines[i].Split(':');
						String[] bits = parts[1].Split(';');
						isAttachment = bits[0].Trim() == "attachment";

						for (int x = 0; x < bits.Length; x++)
						{
							bits[x] = bits[x].Trim();
							if (bits[x].StartsWith("filename=\""))
							{
								filename = bits[x].Substring(10, bits[x].Length - 11);
							}
							else if (bits[x].StartsWith("filename="))
							{
								filename = bits[x].Substring(9, bits[x].Length - 9);
							}
						}
					}
					else if (lines[i].StartsWith("Content-Transfer-Encoding"))
					{
						transportEncoding = lines[i].Substring(27);
					}

					i++;
				}

				if (newBound != "")
				{
					//check for new section
					ParseMessageSection(newBound, lines, ref i);
				}

				if (MimeTypeHandler != null && MimeTypeHandler(currentType, lines, ref i, this)) continue;

				StringBuilder messageBuilder = new StringBuilder();

				for (; i < lines.Length; i++)
				{
					if (lines[i] == "--" + bound) break;
					if (lines[i] == "--" + bound + "--") break;

					if (isAttachment)
						messageBuilder.Append(lines[i]);
					else
						messageBuilder.AppendLine(lines[i]);
				}

				i--;

				if (isAttachment)
				{
					Imap4Attachment currentAttachment = new Imap4Attachment
					{
						Name = filename,
						Type = currentType,
						Data = Convert.FromBase64String(messageBuilder.ToString())
					};

					//add to attachment list
					Attachments.Add(currentAttachment);
				}
				else 
					switch (currentType)
					{
						case "text/plain":
							BodyText = messageBuilder.ToString().Trim();
							break;
						case "text/html":
							BodyHtml = messageBuilder.ToString().Trim();

							if (transportEncoding == "base64")
								BodyHtml = Encoding.ASCII.GetString(Convert.FromBase64String(BodyHtml));

							break;
					}
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="raw"></param>
		/// <returns></returns>
		public Imap4Message ParseRawMessage(String raw)
		{
			char[] separators = { '\n' };
			char[] headerSeparators = { ':' };
			char[] contentSeparators = { ';' };

			Raw = raw;

			String[] lines = raw.Split(separators);

			for (int x = 0; x < lines.Length; x++)
			{
				lines[x] = lines[x].TrimEnd('\r');
			}

			int i;
			for (i = 0; i < lines.Length; i++)
			{
				if (lines[i].Trim().Length == 0) break;

				String currentHeader = lines[i].Trim();

				while (lines[i + 1].StartsWith("\t") || lines[i + 1].StartsWith(" "))
				{
					currentHeader = currentHeader.Trim() + " " + lines[++i].Trim();
				}


				String[] parts = currentHeader.Split(headerSeparators, 2);

				Diagnostics.Debug.WriteLine(currentHeader);

				Headers.Add(new Imap4Header(parts[0], parts[1].Trim()));
			}

			//find some special headers to make them easier to use
			foreach (Imap4Header h in Headers)
			{
				switch (h.Name.ToLower())
				{
					case "subject":
						Subject = h.Value;
						break;
					case "to":
						To = h.Value;
						break;
					case "cc":
						Cc = h.Value;
						break;
					case "bcc":
						Bcc = h.Value;
						break;
					case "from":
						From = h.Value;
						break;
					case "reply-to":
						ReplyTo = h.Value;
						break;
					case "mime-version":
						MimeVersion = Convert.ToDouble(h.Value);
						break;
					case "date":
						{
							try
							{
								// sometimes a string like this is received:
								// Thu, 28 May 2020 17:55:01 +0200 (CEST)
								// we need to remove the (CEST) part
								String dtx = h.Value;
								if (dtx.EndsWith(")"))
									dtx = dtx.Substring(0, dtx.LastIndexOf(" "));

								Date = DateTime.Parse(dtx);
							}
							catch (Exception)
							{
								Date = DateTime.Now;
							}
						}
						break;
					case "content-type":
						{
							String type = h.Value;
							if (type.Contains(";"))
							{
								//ContentBoundary
								String[] contentParts = type.Split(contentSeparators);

								ContentType = contentParts[0];

								for (int x = 1; x < contentParts.Length; x++)
								{
									contentParts[x] = contentParts[x].Trim();
									if (contentParts[x].StartsWith("boundary=\""))
									{
										ContentBoundary = contentParts[x].Substring(10, contentParts[x].Length - 11);
									}
									else if (contentParts[x].StartsWith("boundary"))
									{
										ContentBoundary = contentParts[x].Substring(9, contentParts[x].Length - 9);
									}
								}
							}
							else
							{
								ContentType = h.Value;
							}
						}
						break;
					case "htmlbody":
						BodyHtml = h.Value;
						break;
					case "plaintext":
						BodyText = h.Value;
						break;
				}
			}

			if (Headers["References"] != null || Headers["In-Reply-To"] != null)
			{
				IsReply = true;
			}

			//do we have an outside parser?
			//if so, ask them do they want to handle this message type
			if (MimeTypeHandler == null || !MimeTypeHandler(ContentType, lines, ref i, this))
			{
				switch (ContentType)
				{
					case "text/plain":
						{
							//plain text message
							StringBuilder bodyText = new StringBuilder();
							for (i++; i < lines.Length; i++)
							{
								bodyText.AppendLine(lines[i].TrimEnd());
							}
							BodyText = bodyText.ToString();
						}
						break;
					case "text/html":
						{
							//plain html message
							StringBuilder bodyHtml = new StringBuilder();
							for (i++; i < lines.Length; i++)
							{
								bodyHtml.AppendLine(lines[i].TrimEnd());
							}
							BodyHtml = bodyHtml.ToString();
						}
						break;
					default:
						ParseMessageSection(ContentBoundary, lines, ref i);
						break;
				}
			}

			return this;
		}
	}
}