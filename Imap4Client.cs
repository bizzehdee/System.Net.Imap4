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
using System.Net.Sockets;
using System.Net.Security;
using System.IO;

namespace System.Net.Imap4
{
	/// <summary>
	/// Working implementation of the IMAP4 client protocol
	/// </summary>
	public class Imap4Client
	{
		/// <summary>
		/// Delegate for interupt to a Wait() call
		/// </summary>
        /// <param name="sender">Imap4Client instance which triggered this call</param>
		/// <param name="res">Response recieved</param>
		public delegate void WaitInteruptedD(object sender, String res);

		/// <summary>
		/// Event called when a call to Wait() is interupted
		/// </summary>
		public event WaitInteruptedD WaitInterupted;

		/// <summary>
        /// Delegate for interupt to a Wait() call with a new mail
		/// </summary>
        /// <param name="sender">Imap4Client instance which triggered this call</param>
        /// <param name="res">Response recieved</param>
		public delegate void NewMailReceivedD(object sender, String res);

		/// <summary>
        /// Event called when a call to Wait() is interupted with a new mail
		/// </summary>
		public event NewMailReceivedD NewMailReceived;

		/// <summary>
		/// The current folder the client is using
		/// </summary>
		public String CurrentFolder { get; private set; }
		/// <summary>
		/// Total number of availble emails
		/// </summary>
		public UInt32 AvailableEmails { get; private set; }
		/// <summary>
		/// Number of recent emails
		/// </summary>
		public UInt32 RecentEmails { get; private set; }
		/// <summary>
		/// Number of unread emails
		/// </summary>
		public UInt32 UnreadEmails { get; private set; }

		/// <summary>
		/// Imap4 client implementation
		/// </summary>
		public Imap4Client()
		{
			CurrentFolder = "";
			_client = new TcpClient();
			AvailableEmails = 0;
			RecentEmails = 0;
			UnreadEmails = 0;
		}

		/// <summary>
		/// Connect to imap4 server
		/// </summary>
		/// <param name="server">name or ip of server to connect to</param>
		/// <param name="port">port number to use</param>
		/// <param name="ssl">should we use ssl to connect</param>
		public void Connect(String server, int port, bool ssl)
		{
			_useSSL = ssl;
			_client.Connect(server, port);

			_networkStream = _client.GetStream();

			if (_useSSL)
			{
				_sslStream = new SslStream(_networkStream, true);

				try
				{
					_sslStream.AuthenticateAsClient(server);
				}
				catch (Exception)
				{

					return;
				}

				_stream = _sslStream;
			}
			else
			{
				_stream = _networkStream;
			}

			string response = Response();

			if (response.Substring(0, 4) != "* OK")
			{
				throw new Imap4Exception(response);
			}
		}

		/// <summary>
		/// Connect to imap4 server (SSL turned off)
		/// </summary>
		/// <param name="server">name or ip of server to connect to</param>
		/// <param name="port">should we use ssl to connect</param>
		public void Connect(String server, int port)
		{
			Connect(server, port, false);
		}

		/// <summary>
		/// Connect to imap4 server (SSL turned off, using port 143)
		/// </summary>
		/// <param name="server">name or ip of server to connect to</param>
		public void Connect(String server)
		{
			Connect(server, 143);
		}

		/// <summary>
		/// Send logoff command and disconnect from server
		/// </summary>
		public void Disconnect()
		{
			Write(". logout");

			string response = Response();

			if (response.Substring(0, 5) != "* BYE")
			{
				throw new Imap4Exception(response);
			}

			_client.Close();
		}

		/// <summary>
		/// Send username and password authorisation
		/// supports only AUTH=PLAIN for the moment
		/// </summary>
		/// <param name="user">email account username</param>
		/// <param name="pass">email account password</param>
		public void SendAuthUserPass(String user, String pass)
		{
			Write(". login " + user + " " + pass);

			string response = Response();

			if (response.Substring(0, 4) != ". OK")
			{
				throw new Imap4Exception(response);
			}
		}

		/// <summary>
		/// List all folders based in the filter
		/// </summary>
		/// <param name="filter">Filter to apply to required list</param>
		/// <returns>List of all folders matching the filter</returns>
		public IEnumerable<String> ListFolders(String filter = "*")
		{
			String response;
			char[] trimChars = new[] { ' ', '\t', '\r', '\n', '\"' };

			Write(". list \"\" \"" + filter + "\"");

			do
			{
				response = Response();

				if (!response.StartsWith("*")) continue;

				String[] parts = response.Split(' ');
				String folder = parts[parts.Length - 1].Trim(trimChars);

				yield return folder;
			} while (response.StartsWith("*"));
		}

		/// <summary>
		/// Select the folder to use
		/// </summary>
		/// <param name="folder">Name of the folder to use. e.g. INBOX</param>
		public void SelectFolder(String folder)
		{
			String response;

			Write(". select " + folder);
			CurrentFolder = folder;

			do
			{
				response = Response();

				if (response.Contains("EXISTS"))
				{
					String[] parts = response.Split(' ');
					AvailableEmails = Convert.ToUInt32(parts[1]);
				}
				else if (response.Contains("RECENT"))
				{
					String[] parts = response.Split(' ');
					RecentEmails = Convert.ToUInt32(parts[1]);
				}
				else if (response.Contains("UNSEEN"))
				{
					String unseen = response.Substring(response.IndexOf("UNSEEN", StringComparison.Ordinal) + 7, 3);
					RecentEmails = Convert.ToUInt32(unseen);
				}
			} while (response.StartsWith("*"));

			if (response.Substring(0, 4) == ". OK") return;

			CurrentFolder = "";
			AvailableEmails = 0;
			RecentEmails = 0;
			RecentEmails = 0;

			throw new Imap4Exception(response.Substring(5));
		}

		/// <summary>
		/// Get the total number of emails in this folder, same as AvailableEmails
		/// </summary>
		/// <returns>AvailableEmails</returns>
		public UInt32 GetEmailCount()
		{
			return AvailableEmails;
		}

		/// <summary>
		/// Returns raw mail data for <param name="id">specified id</param>
		/// </summary>
		/// <param name="id">Mail ID</param>
		/// <returns>Raw mail data</returns>
		public String GetEmailRaw(UInt32 id)
		{
			StringBuilder raw = new StringBuilder("");

			Write(". fetch " + id + " body[]");

			string response = Response();

			if (response.Contains(id + " FETCH"))
			{
				do
				{
					response = Response();

					if (!response.StartsWith(".") && !response.StartsWith(")") && !response.StartsWith("*"))
					{
						raw.Append(response);
					}

				} while (!response.StartsWith(". OK"));
			}

			if (response.Contains(" NO"))
			{
				throw new Imap4Exception(response);
			}

			return raw.ToString();
		}

		/// <summary>
        /// Returns parsed mail data as Imap4Message for <paramref name="id">specified id</paramref>
		/// </summary>
        /// <param name="id">Mail ID</param>
        /// <returns>Imap4Message parsed mail</returns>
		public Imap4Message GetEmail(UInt32 id)
		{
			String messageRaw = GetEmailRaw(id);

			Imap4Message message = new Imap4Message();

			message.ParseRawMessage(messageRaw);

			return message;
		}

		/// <summary>
		/// Sets the deleted flag and moves to \\Deleted folder
		/// </summary>
        /// <param name="id">Mail ID</param>
		public void DeleteEmail(UInt32 id)
		{
			String response;

			Write(". UID STORE " + id + " +FLAGS (\\Deleted)");

			do
			{
				response = Response();

			} while (!response.StartsWith("."));

			if (!response.StartsWith(". OK"))
			{
				throw new Imap4Exception(response.Substring(5));
			}

			Write(". EXPUNGE");

			do
			{
				response = Response();

			} while (!response.StartsWith("."));

			if (!response.StartsWith(". OK"))
			{
				throw new Imap4Exception(response.Substring(5));
			}
		}

		/// <summary>
		/// Causes the client to wait for the server to do something/anything
		/// </summary>
		/// <returns>Reason for cancelling wait</returns>
		public String Wait()
		{
			String response;

			Write(". idle");
			_waiting = true;

			do
			{
				response = Response();

				if (response.StartsWith("+")) continue;

				_waiting = false;

				if (response.Contains("RECENT"))
				{
					if (NewMailReceived != null) NewMailReceived(this, response);
				}
				else
				{
					if (WaitInterupted != null) WaitInterupted(this, response);
				}

			} while (!response.StartsWith("."));

			return response;
		}

		/// <summary>
		/// Cancels a previous call to Wait()
		/// </summary>
		/// <returns></returns>
		public String CancelWait()
		{
			if (_waiting) Write("done");
			_waiting = false;

			string response = Response();

			return response;
		}

		/// <summary>
		/// Set a flag on an email
		/// </summary>
		/// <param name="id">ID of the email to set</param>
		/// <param name="flag">Flag to set, must begin with backslash</param>
		/// <returns>true on set, false on not set</returns>
		public bool SetFlag(UInt32 id, String flag)
		{
			String response;

			if (!flag.StartsWith("\\"))
			{
				throw new Imap4Exception("Invalid Flag");
			}

			Write(". store " + id + " +flags " + flag);

			do
			{
				response = Response();

			} while (!response.StartsWith("."));

			return response.Contains("OK STORE");
		}

		/// <summary>
        /// Unset a flag on an email
		/// </summary>
        /// <param name="id">ID of the email to unset</param>
        /// <param name="flag">Flag to unset, must begin with backslash</param>
        /// <returns>true on unset, false on not unset</returns>
		public bool RemoveFlag(UInt32 id, String flag)
		{
			String response;

			if (!flag.StartsWith("\\"))
			{
				throw new Imap4Exception("Invalid Flag");
			}

			Write(". store " + id + " -flags " + flag);

			do
			{
				response = Response();

			} while (!response.StartsWith("."));

			return response.Contains("OK STORE");
		}

		/// <summary>
		/// Shortcut for SetFlag, with Seen already set
		/// </summary>
		/// <param name="id">ID of email to mark as read</param>
		/// <returns>true on set, false on not set</returns>
		public bool MarkAsRead(UInt32 id)
		{
			return SetFlag(id, "\\Seen");
		}

		#region Privates

		private bool _waiting;
		private readonly TcpClient _client;
		private bool _useSSL;
		private SslStream _sslStream;
		private NetworkStream _networkStream;
		private Stream _stream;

		private void Write(String str)
		{
			ASCIIEncoding encoder = new ASCIIEncoding();

			if (!str.EndsWith("\r\n")) str += "\r\n";

			byte[] buffer = encoder.GetBytes(str);

#if DEBUG
			Diagnostics.Debug.WriteLine(">> " + str);
#endif

			_stream.Write(buffer, 0, buffer.Length);
		}

		private string Response()
		{
			ASCIIEncoding encoder = new ASCIIEncoding();
			StringBuilder returnBuilder = new StringBuilder();

			byte[] readBuffer = new byte[1];

			while (true)
			{
				readBuffer[0] = 0;
				int bytesRead = _stream.Read(readBuffer, 0, 1);

				if (bytesRead != 1)
				{
					break;
				}

				returnBuilder.Append(encoder.GetString(readBuffer, 0, 1));

				if (readBuffer[0] == '\n')
				{
					break;
				}
			}
#if DEBUG
			Diagnostics.Debug.WriteLine("<< " + returnBuilder);
#endif

			return returnBuilder.ToString();
		}

		#endregion
	}
}
