﻿using System;
using System.IO;
using System.Net;
using System.Text;
using UnityEngine;
using ComponentAce.Compression.Libs.zlib;
using System.Threading.Tasks;
using System.Security.Policy;
using System.Collections.Generic;
using System.Runtime.Remoting.Messaging;

namespace SIT.Tarkov.Core
{
    public class Request : IDisposable
    {
        public static string Session;
        public static string RemoteEndPoint;
        public bool isUnity;
        private Dictionary<string, string> m_RequestHeaders;

        public Request()
        {
            //if(string.IsNullOrEmpty(Session))
            //    Session = PatchConstants.GetPHPSESSID();
            if (string.IsNullOrEmpty(RemoteEndPoint))
                RemoteEndPoint = PatchConstants.GetBackendUrl();


            string[] args = Environment.GetCommandLineArgs();

            foreach (string arg in args)
            {
                //if (arg.Contains("BackendUrl"))
                //{
                //    string json = arg.Replace("-config=", string.Empty);
                //    _host = Json.Deserialize<ServerConfig>(json).BackendUrl;
                //}

                if (arg.Contains("-token="))
                {
                    Session = arg.Replace("-token=", string.Empty);
                    m_RequestHeaders = new Dictionary<string, string>()
                    {
                        { "Cookie", $"PHPSESSID={Session}" },
                        { "SessionId", Session }
                    };
                }
            }
        }

        public Request(string session, string remoteEndPoint, bool isUnity = true)
        {
            Session = session;
            RemoteEndPoint = remoteEndPoint;
        }
        /// <summary>
        /// Send request to the server and get Stream of data back
        /// </summary>
        /// <param name="url">String url endpoint example: /start</param>
        /// <param name="method">POST or GET</param>
        /// <param name="data">string json data</param>
        /// <param name="compress">Should use compression gzip?</param>
        /// <returns>Stream or null</returns>
        private Stream Send(string url, string method = "GET", string data = null, bool compress = true, int timeout = 1500)
        {

           
            // disable SSL encryption
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };

            var fullUri = url;
            if (!Uri.IsWellFormedUriString(fullUri, UriKind.Absolute))
                fullUri = RemoteEndPoint + fullUri;

            //PatchConstants.Logger.LogInfo(fullUri);

            var uri = new Uri(fullUri);
            if (uri.Scheme == "https")
            {
                // disable SSL encryption
                ServicePointManager.Expect100Continue = true;
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };

            }

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.ServerCertificateValidationCallback = delegate { return true; };
            //var request = WebRequest.CreateHttp(fullUri);

            //if (!string.IsNullOrEmpty(Session))
            //{
            //    request.Headers.Add("Cookie", $"PHPSESSID={Session}");
            //    request.Headers.Add("SessionId", Session);
            //}
            foreach(var item in m_RequestHeaders)
            {
                request.Headers.Add(item.Key, item.Value);
            }

            request.Headers.Add("Accept-Encoding", "deflate");

            request.Method = method;
            request.Timeout = timeout;

            if (method != "GET" && !string.IsNullOrEmpty(data))
            {
                // set request body
                byte[] bytes = (compress) ? SimpleZlib.CompressToBytes(data, zlibConst.Z_BEST_COMPRESSION) : Encoding.UTF8.GetBytes(data);

                request.ContentType = "application/json";
                request.ContentLength = bytes.Length;

                if (compress)
                {
                    request.Headers.Add("content-encoding", "deflate");
                }

                try
                {
                    using (Stream stream = request.GetRequestStream())
                    {
                        stream.Write(bytes, 0, bytes.Length);
                    }
                }
                catch (Exception e)
                {
                    if (isUnity)
                        Debug.LogError(e);
                }
            }

            // get response stream
            try
            {
                WebResponse response = request.GetResponse();
                return response.GetResponseStream();
            }
            catch (Exception e)
            {
                if (isUnity)
                    Debug.LogError(e);
            }

            return null;
        }

        public byte[] GetData(string url, bool hasHost = false)
        {
            var ms = new MemoryStream();
            var dataStream = Send(url, "GET");
            if (dataStream != null)
            {
                dataStream.CopyTo(ms);

                return ms.ToArray();
            }
            return null;
        }

        public void PutJson(string url, string data, bool compress = true)
        {
            using (Stream stream = Send(url, "PUT", data, compress)) { }
        }

        public string GetJson(string url, bool compress = true)
        {
            using (Stream stream = Send(url, "GET", null, compress))
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    if (stream == null)
                        return "";
                    stream.CopyTo(ms);
                    return SimpleZlib.Decompress(ms.ToArray(), null);
                }
            }
        }

        public string PostJson(string url, string data, bool compress = true)
        {
            using (Stream stream = Send(url, "POST", data, compress))
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    if (stream == null)
                        return "";
                    stream.CopyTo(ms);
                    return SimpleZlib.Decompress(ms.ToArray(), null);
                }
            }
        }

        public async Task<string> PostJsonAsync(string url, string data, bool compress = true)
        {
            return await Task.Run(() => { return PostJson(url, data, compress); });
        }

        public Texture2D GetImage(string url, bool compress = true)
        {
            using (Stream stream = Send(url, "GET", null, compress))
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    if (stream == null)
                        return null;
                    Texture2D texture = new Texture2D(8, 8);

                    stream.CopyTo(ms);
                    texture.LoadImage(ms.ToArray());
                    return texture;
                }
            }
        }

        public void Dispose()
        {
        }
    }
}
