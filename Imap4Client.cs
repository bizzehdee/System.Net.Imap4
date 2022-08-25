﻿/*
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
using System.Linq;
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
        public delegate void WaitInteruptedD(object sender, string res);

        /// <summary>
        /// Event called when a call to Wait() is interupted
        /// </summary>
        public event WaitInteruptedD WaitInterupted;

        /// <summary>
        /// Delegate for interupt to a Wait() call with a new mail
        /// </summary>
        /// <param name="sender">Imap4Client instance which triggered this call</param>
        /// <param name="res">Response recieved</param>
        public delegate void NewMailReceivedD(object sender, string res);

        /// <summary>
        /// Event called when a call to Wait() is interupted with a new mail
        /// </summary>
        public event NewMailReceivedD NewMailReceived;

        /// <summary>
        /// The current folder the client is using
        /// </summary>
        public string CurrentFolder { get; private set; }
        /// <summary>
        /// Total number of availble emails
        /// </summary>
        public uint AvailableEmails { get; private set; }
        /// <summary>
        /// Number of recent emails
        /// </summary>
        public uint RecentEmails { get; private set; }
        /// <summary>
        /// Number of unread emails
        /// </summary>
        public uint UnreadEmails { get; private set; }
        /// <summary>
        /// List of capabilities recieved from the server
        /// </summary>
        public IEnumerable<string> ServerCapabilities { get; private set; }

        public enum AuthTypes
        {
            Plain,
            XOAuth2
        }

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
        public void Connect(string server, int port, bool ssl)
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

            var response = Response();

            if (response.Substring(0, 4) != "* OK")
            {
                throw new Imap4Exception(response);
            }

            ServerCapabilities = GetServerCapabilities();
        }

        /// <summary>
        /// Connect to imap4 server (SSL turned off)
        /// </summary>
        /// <param name="server">name or ip of server to connect to</param>
        /// <param name="port">should we use ssl to connect</param>
        public void Connect(string server, int port)
        {
            Connect(server, port, false);
        }

        /// <summary>
        /// Connect to imap4 server (SSL turned off, using port 143)
        /// </summary>
        /// <param name="server">name or ip of server to connect to</param>
        public void Connect(string server)
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
        /// 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetServerCapabilities()
        {
            Write(". CAPABILITY");
            string response;
            do
            {
                response = Response();

                if (response.StartsWith("* CAPABILITY"))
                {
                    string capabilities = response.Substring(12).Trim();
                    foreach (string s in capabilities.Split(' '))
                    {
                        yield return s;
                    }
                }

            } while (!response.StartsWith("."));

            if (response.Substring(0, 4) != ". OK")
            {
                throw new Imap4Exception(response);
            }
        }

        /// <summary>
        /// Send username and password authorisation
        /// supports only AUTH=PLAIN and AUTH=XOAUTH2 for now
        /// </summary>
        /// <param name="user">email account username</param>
        /// <param name="pass">email account password or oauth2 token</param>
        /// <param name="authType">Authorisation type to use</param>
        public void SendAuthUserPass(string user, string pass, AuthTypes authType = AuthTypes.Plain)
        {
            byte[] authBytes = { 0 };
            string response;

            if (authType == AuthTypes.Plain)
            {
                Write(". AUTHENTICATE PLAIN");
                response = Response();

                if (!response.StartsWith("+"))
                {
                    throw new Imap4Exception(response);
                }

                authBytes = authBytes.Concat(Encoding.ASCII.GetBytes(user)).Concat(new byte[] { 0 }).Concat(Encoding.ASCII.GetBytes(pass)).ToArray();

                Write(Convert.ToBase64String(authBytes));

            }
            else if (authType == AuthTypes.XOAuth2)
            {
                Write(". AUTHENTICATE XOAUTH2");
                response = Response();

                if (!response.StartsWith("+"))
                {
                    throw new Imap4Exception(response);
                }

                authBytes = Encoding.ASCII.GetBytes("user=");
                authBytes = authBytes
                    .Concat(Encoding.ASCII.GetBytes(user))
                    .Concat(new byte[] { 1 })
                    .Concat(Encoding.ASCII.GetBytes(string.Format("auth=Bearer {0}", pass)))
                    .Concat(new byte[] { 1, 1 }).ToArray();
            }
            else
            {
                throw new Imap4Exception("Authentication type not supported");
            }

            Write(Convert.ToBase64String(authBytes));

            response = Response();

            while (!response.StartsWith("."))
            {
                response = Response();
            }

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
        public IEnumerable<string> ListFolders(string filter = "*")
        {
            string response;
            char[] trimChars = new[] { ' ', '\t', '\r', '\n', '\"' };

            Write(". list \"\" \"" + filter + "\"");

            do
            {
                response = Response();

                if (!response.StartsWith("*")) continue;

                string[] parts = response.Split(' ');
                string folder = parts[parts.Length - 1].Trim(trimChars);

                yield return folder;
            } while (response.StartsWith("*"));
        }

        /// <summary>
        /// Select the folder to use
        /// </summary>
        /// <param name="folder">Name of the folder to use. e.g. INBOX</param>
        public void SelectFolder(string folder)
        {
            string response;

            Write(string.Format(". select \"{0}\"", folder));
            CurrentFolder = folder;

            do
            {
                response = Response();

                if (response.Contains("EXISTS"))
                {
                    string[] parts = response.Split(' ');
                    AvailableEmails = Convert.ToUInt32(parts[1]);
                }
                else if (response.Contains("RECENT"))
                {
                    string[] parts = response.Split(' ');
                    RecentEmails = Convert.ToUInt32(parts[1]);
                }
                else if (response.Contains("UNSEEN"))
                {
                    string unseen = response.Substring(response.IndexOf("UNSEEN", StringComparison.Ordinal) + 7, 3);
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
        public uint GetEmailCount()
        {
            return AvailableEmails;
        }

        /// <summary>
        /// Returns raw mail data for <param name="id">specified id</param>
        /// </summary>
        /// <param name="id">Mail ID</param>
        /// <returns>Raw mail data</returns>
        public string GetEmailRaw(uint id)
        {
            StringBuilder raw = new("");

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
        public Imap4Message GetEmail(uint id)
        {
            var messageRaw = GetEmailRaw(id);

            Imap4Message message = new();

            message.ParseRawMessage(messageRaw);

            return message;
        }

        /// <summary>
        /// Sets the deleted flag and moves to \\Deleted folder
        /// </summary>
        /// <param name="id">Mail ID</param>
        public void DeleteEmail(uint id)
        {
            string response;

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
        public string Wait()
        {
            string response;

            Write(". idle");
            _waiting = true;

            do
            {
                response = Response();

                if (response.StartsWith("+")) continue;

                _waiting = false;

                if (response.Contains("RECENT"))
                {
                    NewMailReceived?.Invoke(this, response);
                }
                else
                {
                    WaitInterupted?.Invoke(this, response);
                }

            } while (!response.StartsWith("."));

            return response;
        }

        /// <summary>
        /// Cancels a previous call to Wait()
        /// </summary>
        /// <returns></returns>
        public string CancelWait()
        {
            if (_waiting) Write("done");
            _waiting = false;

            var response = Response();

            return response;
        }

        /// <summary>
        /// Set a flag on an email
        /// </summary>
        /// <param name="id">ID of the email to set</param>
        /// <param name="flag">Flag to set, must begin with backslash</param>
        /// <returns>true on set, false on not set</returns>
        public bool SetFlag(uint id, string flag)
        {
            string response;

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
        public bool RemoveFlag(uint id, string flag)
        {
            string response;

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
        public bool MarkAsRead(uint id)
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

        private void Write(string str)
        {
            ASCIIEncoding encoder = new();

            if (!str.EndsWith("\r\n")) str += "\r\n";

            byte[] buffer = encoder.GetBytes(str);

#if DEBUG
            Diagnostics.Debug.WriteLine(">> " + str);
#endif

            _stream.Write(buffer, 0, buffer.Length);
        }

        private string Response()
        {
            ASCIIEncoding encoder = new();
            StringBuilder returnBuilder = new();

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
