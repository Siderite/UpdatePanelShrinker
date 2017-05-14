using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web;
using System.Web.Caching;
using System.Web.UI;
using Siderite.Web.Handlers;

namespace Siderite.Web.Ajax
{
    /// <summary>
    /// Shrinks <see cref="UpdatePanel"/> output based on previous content
    /// Use it where network bandwidth is limited
    /// </summary>
    [Serializable]
    public class UpdatePanelShrinkerFilter : Stream
    {

        private static readonly Random _rnd = new Random();

        private static int _maximumCacheSize = 1000000000;
        private static double _maximumCacheTime = TimeSpan.FromMinutes(15).TotalMilliseconds;

        /// <summary>
        /// holds the previous versions of the renderings
        /// </summary>
        private static Dictionary<string, string> _previousRender;

        private static Dictionary<string, DateTime> _previousRenderTime;

        private readonly Page _page;
        private string _key;
        private int _maxLevel = 10;
        private int _minLength = 1000;
        private StringBuilder _oOutput;

        /// <summary>
        /// holds the relationships between update panels
        /// </summary>
        private Dictionary<string, string> _panelTree;

        private int _searchLength = 100;
        private string _sessionID;
        private int _tryCount = 20;
        private bool _isHtml;
        private Encoding _encoding;
        private MemoryStream _originalMemoryStream;

        /// <summary>
        /// Use this in Page.Load
        /// </summary>
        /// <param name="sink">Response.Filter</param>
        /// <param name="page">Current page</param>
        /// (an error will be displayed if the update fails instead)
        public UpdatePanelShrinkerFilter(Stream sink, Page page)
            : this(sink, page, true)
        {
        }

        /// <summary>
        /// Use this in Page.Load
        /// </summary>
        /// <param name="sink">Response.Filter</param>
        /// <param name="page">Current page</param>
        /// <param name="useHandler">use false if you don't want a command handler installed to check on update success 
        /// (an error will be displayed if the update fails instead)</param>
        public UpdatePanelShrinkerFilter(Stream sink, Page page, bool useHandler)
        {
            if (page == null) throw new ArgumentNullException("page", "Page cannot be null.");
            if (useHandler) InstallHandler();
            Sink = sink;
            _page = page;
            Request = page.Request;
            _isHtml = true;
            CheckHtmlAndEncoding();
            InitFilter();
        }


        /// <summary>
        /// holds the previous versions of the renderings
        /// </summary>
        private static Dictionary<string, string> PreviousRender
        {
            get
            {
                if (_previousRender == null)
                {
                    _previousRender = new Dictionary<string, string>();
                    if (HttpRuntime.Cache != null)
                    {
                        if
                            (HttpRuntime.Cache["AjaxPage_PreviousRender"] != null)
                            _previousRender =
                                (Dictionary<string, string>) HttpRuntime.Cache["AjaxPage_PreviousRender"];
                        else
                        {
                            HttpRuntime.Cache.Add("AjaxPage_PreviousRender", _previousRender, null,
                                                  Cache.NoAbsoluteExpiration, TimeSpan.FromHours(1),
                                                  CacheItemPriority.NotRemovable, null);
                        }
                    }
                }
                return _previousRender;
            }
        }

        /// <summary>
        /// holds the time of the previous renderings
        /// </summary>
        private static Dictionary<string, DateTime> PreviousRenderTime
        {
            get
            {
                if (_previousRenderTime == null)
                {
                    _previousRenderTime = new Dictionary<string, DateTime>();
                    if (HttpRuntime.Cache != null)
                    {
                        if
                            (HttpRuntime.Cache["AjaxPage_PreviousRenderTime"] != null)
                            _previousRenderTime =
                                (Dictionary<string, DateTime>) HttpRuntime.Cache["AjaxPage_PreviousRenderTime"];
                        else
                        {
                            HttpRuntime.Cache.Add("AjaxPage_PreviousRenderTime", _previousRenderTime, null,
                                                  Cache.NoAbsoluteExpiration, TimeSpan.FromHours(1),
                                                  CacheItemPriority.NotRemovable, null);
                        }
                    }
                }
                return _previousRenderTime;
            }
        }


        private Dictionary<string, string> PanelTree
        {
            get { return _panelTree ?? (_panelTree = new Dictionary<string, string>()); }
        }

        ///<summary>
        ///When overridden in a derived class, gets a value indicating whether the current stream supports reading.
        ///</summary>
        ///
        ///<returns>
        ///true if the stream supports reading; otherwise, false.
        ///</returns>
        ///<filterpriority>1</filterpriority>
        public override bool CanRead
        {
            get { return Sink == null || Sink.CanRead; }
        }

        ///<summary>
        ///When overridden in a derived class, gets a value indicating whether the current stream supports seeking.
        ///</summary>
        ///
        ///<returns>
        ///true if the stream supports seeking; otherwise, false.
        ///</returns>
        ///<filterpriority>1</filterpriority>
        public override bool CanSeek
        {
            get { return Sink == null || Sink.CanSeek; }
        }

        ///<summary>
        ///When overridden in a derived class, gets a value indicating whether the current stream supports writing.
        ///</summary>
        ///
        ///<returns>
        ///true if the stream supports writing; otherwise, false.
        ///</returns>
        ///<filterpriority>1</filterpriority>
        public override bool CanWrite
        {
            get { return Sink == null || Sink.CanWrite; }
        }

        ///<summary>
        ///When overridden in a derived class, gets the length in bytes of the stream.
        ///</summary>
        ///
        ///<returns>
        ///A long value representing the length of the stream in bytes.
        ///</returns>
        ///
        ///<exception cref="T:System.NotSupportedException">A class derived from Stream does not support seeking. </exception>
        ///<exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception><filterpriority>1</filterpriority>
        public override long Length
        {
            get { return Sink == null ? 0 : Sink.Length; }
        }

        ///<summary>
        ///When overridden in a derived class, gets or sets the position within the current stream.
        ///</summary>
        ///
        ///<returns>
        ///The current position within the stream.
        ///</returns>
        ///
        ///<exception cref="T:System.IO.IOException">An I/O error occurs. </exception>
        ///<exception cref="T:System.NotSupportedException">The stream does not support seeking. </exception>
        ///<exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception><filterpriority>1</filterpriority>
        public override long Position
        {
            get { return Sink == null ? 0 : Sink.Position; }
            set { if (Sink != null) Sink.Position = value; }
        }

        ///<summary>
        ///base Filter (this should be set from the constructor to Response.Filter)
        ///</summary>
        public Stream Sink { get; set; }

        ///<summary>
        ///current Page (should be set from the constructor)
        ///</summary>
        public Page Page
        {
            get { return _page; }
        }

        private StringBuilder OOutput
        {
            get { return _oOutput ?? (_oOutput = new StringBuilder()); }
        }

        /// <summary>
        /// How many tries to compress. The smaller the number, the faster and the smaller the compression.
        /// 0 means only trimming of the identical start and end
        /// default 10.
        /// </summary>
        public int MaxLevel
        {
            get { return _maxLevel; }
            set { _maxLevel = value; }
        }

        /// <summary>
        /// How large should be the seed used to find common substrings
        /// default 100. Should not be small, or it'll get false positives and not large because it won't find anything.
        /// </summary>
        public int SearchLength
        {
            get { return _searchLength; }
            set { _searchLength = value; }
        }

        /// <summary>
        /// How many times should the algorithm try to find a common substring in substrings
        /// default 20.
        /// </summary>
        public int TryCount
        {
            get { return _tryCount; }
            set { _tryCount = value; }
        }

        /// <summary>
        /// The minimum length of string which the algorithm will not try to shrink.
        /// default 1000.
        /// </summary>
        public int MinLength
        {
            get { return _minLength; }
            set { _minLength = value; }
        }

        private string Key
        {
            get
            {
                if (_key == null)
                {
                    EnsureKey();
                }
                return _sessionID + ":" + _key;
            }
        }

        private void EnsureKey()
        {
            if (Request["UpdatePanelShrinkerFilter_key"] == null)
            {
                _key = Guid.NewGuid() + "-" + DateTime.Now.ToBinary();
                ScriptManager.RegisterHiddenField(Page, "UpdatePanelShrinkerFilter_key", _key);
            }
            else
            {
                _key = Request["UpdatePanelShrinkerFilter_key"];
            }
        }

        /// <summary>
        /// Maximum time a render stays in cache in milliseconds (default 15 minutes)
        /// </summary>
        public static double MaximumCacheTime
        {
            get { return _maximumCacheTime; }
            set { _maximumCacheTime = value; }
        }

        /// <summary>
        /// Maximum cache size in bytes (default one billion bytes)
        /// </summary>
        public static int MaximumCacheSize
        {
            get { return _maximumCacheSize; }
            set { _maximumCacheSize = value; }
        }

        /// <summary>
        /// Instructs the update panel shrinker to use a command handler to check if the update is correct
        /// Defaults to if the handler is installed or if the class was able to change the Web.Config to add it
        /// </summary>
        public bool UseHandler { get; set; }

        public HttpRequest Request { get; private set; }

        public MemoryStream OriginalMemoryStream
        {
            get { return _originalMemoryStream ?? (_originalMemoryStream = new MemoryStream()); }
            set { _originalMemoryStream = value; }
        }

        private void InstallHandler()
        {
            try
            {
                CommandHandler.ModifyWebConfig();
                UseHandler = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Cannot change Web.Config to add the CommandHandler.(" + ex.Message + ")");
                UseHandler = false;
            }
        }

        private void InitFilter()
        {
            var session = HttpContext.Current.Session;
            if (session != null)
            {
                if (session.IsNewSession)
                {
                    session["InitializeSession"] = "Something";
                }
                _sessionID = session.SessionID;
            }
            ScriptManager sm = ScriptManager.GetCurrent(Page);
            if (sm != null)
            {
                if (!sm.IsInAsyncPostBack)
                {
                    if (_sessionID != null)
                    {
                        var keysToRemove = PreviousRender.Keys.Where(key => key.StartsWith(_sessionID + ":")).ToList();
                        foreach (string key in keysToRemove)
                        {
                            PreviousRender.Remove(key);
                            PreviousRenderTime.Remove(key);
                        }
                        EnsureKey();
                    }
                }
                else
                {
                    BuildPanelTree();
                }
                LoadApplyPatch();
            }
            ProcessCache();
        }

        private static void ProcessCache()
        {
            EnforceMaximumCacheTime();
            EnforceMaximumCacheSize();
        }

        private static void EnforceMaximumCacheSize()
        {
            // remove cache entries if the total cache size is above
            // MaximumCacheSize
            int sum = 0;
            var keys = new List<string>();
            foreach (var pair in PreviousRender)
            {
                sum += pair.Value.Length;
                keys.Add(pair.Key);
            }
            Debug.WriteLine(string.Format("Memory footprint: {0} bytes in {1} items.", sum, keys.Count));
            if (sum <= MaximumCacheSize) return;
            int keyNr = keys.Count*sum/MaximumCacheSize;
            for (int c = 0; c < keyNr; c++)
            {
                PreviousRender.Remove(keys[c]);
                PreviousRenderTime.Remove(keys[c]);
            }
        }

        private static void EnforceMaximumCacheTime()
        {
            var keys = new List<string>();
            foreach (var pair in PreviousRender)
            {
                DateTime dt;
                if (!PreviousRenderTime.TryGetValue(pair.Key, out dt)) continue;
                if ((DateTime.Now - dt).TotalMilliseconds > MaximumCacheTime)
                    keys.Add(pair.Key);
            }
            foreach (var key in keys)
            {
                PreviousRender.Remove(key);
                PreviousRenderTime.Remove(key);
            }
        }

        ///<summary>
        ///When overridden in a derived class, clears all buffers for this stream and causes any buffered data to be written to the underlying device.
        ///</summary>
        ///
        ///<exception cref="T:System.IO.IOException">An I/O error occurs. </exception><filterpriority>2</filterpriority>
        public override void Flush()
        {
            if (Sink == null) return;
            FlushShrunkIfNotEmpty();
            Sink.Flush();
        }

        private void FlushShrunkIfNotEmpty()
        {
            if (OOutput.Length > 0||OriginalMemoryStream.Length>0) FlushShrunk();
        }

        private void FlushShrunk()
        {
            if (Sink != null)
            {
                CheckHtmlAndEncoding();
                byte[] data=null;
                bool handled=false;
                if (_isHtml)
                {
                    //Get back the complete response for the client
                    string szCompleteBuffer = OOutput.ToString();

                    szCompleteBuffer = Process(szCompleteBuffer, out handled);

                    data = _encoding.GetBytes(szCompleteBuffer);
                } 
                if (!handled)
                {
                    data = OriginalMemoryStream.ToArray();
                    OriginalMemoryStream.Close();
                }
                try
                {
                    Sink.Write(data, 0, data.Length);
                }
                catch
                {
                    if (HttpContext.Current.IsDebuggingEnabled) throw;
                }
            }
            OriginalMemoryStream=null;
            OOutput.Length = 0;
        }

        private void CheckHtmlAndEncoding()
        {
            if (!_isHtml) return;
            try
            {
                // because calls to Response throw an error in some conditions
                _encoding = _page.Response.ContentEncoding;
                if (!_page.Response.ContentType.StartsWith("text/html", StringComparison.CurrentCultureIgnoreCase)) _isHtml = false;
            }
// ReSharper disable EmptyGeneralCatchClause
            catch
// ReSharper restore EmptyGeneralCatchClause
            {
            }
        }

        private string Process(string content, out bool handled)
        {
            handled = false;
//          Length|Type|id|content|
            var sb = new StringBuilder(content.Length);
            var strings = new[]{"","",""};
            int stage = 0;
            bool ajaxDetected = false;

            int length = 0;
            int pos = 0;
            int i = 0;
            int start = 0;
            while (i<content.Length)
            {
                char ch = content[i];
                if (ch!='|')
                {
                    if (!ajaxDetected)
                    {
                        if (ch < '0' || ch > '9') return content;
                    }
                    strings[stage] += ch;
                } else
                {
                    stage++;
                    switch (stage)
                    {
                        case 1:
                            {
                                if (!int.TryParse(strings[0],out length)) return content;
                                ajaxDetected = true;
                            }
                            break;
                        case 2:
                            break;
                        case 3:
                            {
                                if (content[i + length + 1] != '|') return content;
                                if (strings[1]=="updatePanel")
                                {
                                    sb.Append(content, pos, start-pos);
                                    string id = strings[2];
                                    sb.Append(Shrink(id, content.Substring(i+1, length)));
                                    ClearChildren(id);
                                    pos = i + length + 2;
                                }
                                i += length+1;
                                start = i + 1;

                                stage = 0;
                                strings = new[] { "", "", "" };
                                ajaxDetected = false;
                            }
                            break;
                    }
                }
                i++;
            }
            if (i>pos)
                sb.Append(content, pos, i - pos);
            handled = true;
            return sb.ToString();
        }

        ///<summary>
        ///When overridden in a derived class, sets the position within the current stream.
        ///</summary>
        ///
        ///<returns>
        ///The new position within the current stream.
        ///</returns>
        ///
        ///<param name="offset">A byte offset relative to the origin parameter. </param>
        ///<param name="origin">A value of type <see cref="T:System.IO.SeekOrigin"></see> indicating the reference point used to obtain the new position. </param>
        ///<exception cref="T:System.IO.IOException">An I/O error occurs. </exception>
        ///<exception cref="T:System.NotSupportedException">The stream does not support seeking, such as if the stream is constructed from a pipe or console output. </exception>
        ///<exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception><filterpriority>1</filterpriority>
        public override long Seek(long offset, SeekOrigin origin)
        {
            return Sink != null ? Sink.Seek(offset, origin) : -1;
        }

        ///<summary>
        ///When overridden in a derived class, sets the length of the current stream.
        ///</summary>
        ///
        ///<param name="length">The desired length of the current stream in bytes. </param>
        ///<exception cref="T:System.NotSupportedException">The stream does not support both writing and seeking, such as if the stream is constructed from a pipe or console output. </exception>
        ///<exception cref="T:System.IO.IOException">An I/O error occurs. </exception>
        ///<exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception><filterpriority>2</filterpriority>
        public override void SetLength(long length)
        {
            if (Sink == null) return;
            Sink.SetLength(length);
        }

        ///<summary>
        ///Closes the current stream and releases any resources (such as sockets and file handles) associated with the current stream.
        ///</summary>
        ///<filterpriority>1</filterpriority>
        public override void Close()
        {
            if (Sink == null) return;
            FlushShrunkIfNotEmpty();
            Sink.Close();
        }


        ///<summary>
        ///When overridden in a derived class, reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
        ///</summary>
        ///
        ///<returns>
        ///The total number of bytes read into the buffer. This can be less than the number of bytes requested if that many bytes are not currently available, or zero (0) if the end of the stream has been reached.
        ///</returns>
        ///
        ///<param name="offset">The zero-based byte offset in buffer at which to begin storing the data read from the current stream. </param>
        ///<param name="count">The maximum number of bytes to be read from the current stream. </param>
        ///<param name="buffer">An array of bytes. When this method returns, the buffer contains the specified byte array with the values between offset and (offset + count - 1) replaced by the bytes read from the current source. </param>
        ///<exception cref="T:System.ArgumentException">The sum of offset and count is larger than the buffer length. </exception>
        ///<exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception>
        ///<exception cref="T:System.NotSupportedException">The stream does not support reading. </exception>
        ///<exception cref="T:System.ArgumentNullException">buffer is null. </exception>
        ///<exception cref="T:System.IO.IOException">An I/O error occurs. </exception>
        ///<exception cref="T:System.ArgumentOutOfRangeException">offset or count is negative. </exception><filterpriority>1</filterpriority>
        public override int Read(byte[] buffer, int offset, int count)
        {
            return Sink != null ? Sink.Read(buffer, offset, count) : 0;
        }

        ///<summary>
        ///When overridden in a derived class, writes a sequence of bytes to the current stream and advances the current position within this stream by the number of bytes written.
        ///</summary>
        ///
        ///<param name="offset">The zero-based byte offset in buffer at which to begin copying bytes to the current stream. </param>
        ///<param name="count">The number of bytes to be written to the current stream. </param>
        ///<param name="buffer">An array of bytes. This method copies count bytes from buffer to the current stream. </param>
        ///<exception cref="T:System.IO.IOException">An I/O error occurs. </exception>
        ///<exception cref="T:System.NotSupportedException">The stream does not support writing. </exception>
        ///<exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception>
        ///<exception cref="T:System.ArgumentNullException">buffer is null. </exception>
        ///<exception cref="T:System.ArgumentException">The sum of offset and count is greater than the buffer length. </exception>
        ///<exception cref="T:System.ArgumentOutOfRangeException">offset or count is negative. </exception><filterpriority>1</filterpriority>
        public override void Write(byte[] buffer, int offset, int count)
        {
            OriginalMemoryStream.Write(buffer, offset, count);
            CheckHtmlAndEncoding();
            if (_isHtml)
            {
                //Get a string version of the buffer
                string szBuffer = _encoding.GetString(buffer, offset, count);


                OOutput.Append(szBuffer);
            }

        }

        /// <summary>
        /// needed to clear the previous versions of panel output
        /// if parent update panels are being rendered
        /// </summary>
        private void BuildPanelTree()
        {
            PanelTree.Clear();
            BuildPanelTree(Page, null);
        }

        private void BuildPanelTree(Control control, string parentId)
        {
            if (control is UpdatePanel)
            {
                if (parentId != null) PanelTree[control.ClientID] = parentId;
                foreach (Control ctl in control.Controls)
                    BuildPanelTree(ctl, control.ClientID);
            }
            else
            {
                foreach (Control ctl in control.Controls)
                    BuildPanelTree(ctl, parentId);
            }
        }

        /// <summary>
        /// load the magic script
        /// </summary>
        private void LoadApplyPatch()
        {
            string script = getEmbeddedScript();
            ScriptManager.RegisterClientScriptBlock(Page, Page.GetType(), "loadClientScripts", script, true);
            script = string.Format("UpdatePanelShrinker.ModifyDefaultRenderingMechanism({0});", UseHandler ? "true" : "false");
            ScriptManager.RegisterStartupScript(Page, Page.GetType(), "initShrinker", script, true);
        }

        private string getEmbeddedScript()
        {
            var stream = GetType().Assembly.GetManifestResourceStream("Siderite.Web.Js.updatePanel.js");
            using (stream)
            {
                using (var sr = new StreamReader(stream))
                {
                    return sr.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// clear the cached output of child update panels
        /// </summary>
        /// <param name="id"></param>
        private void ClearChildren(string id)
        {
            foreach (var pair in PanelTree)
            {
                if (pair.Value != id) continue;
                PreviousRender.Remove(Key + ":" + pair.Key);
                PreviousRenderTime.Remove(Key + ":" + pair.Key);
                ClearChildren(pair.Key);
            }
        }

        /// <summary>
        /// try to shrink the update panel output
        /// </summary>
        /// <param name="id"></param>
        /// <param name="content"></param>
        /// <returns></returns>
        private string Shrink(string id, string content)
        {
            string keyId = Key + ":" + id;
            string prev;
            // if the updatepanel content is not cached, cache it and send it
            bool hasPrevious = PreviousRender.TryGetValue(keyId, out prev);
            PreviousRender[keyId] = content;
            PreviousRenderTime[keyId] = DateTime.Now;
            if (hasPrevious)
            {
                // Generate the diff patch
                content = GeneratePatch(content, prev);
            }
            /*// send it through a javascript rather than an updatePanel innerHtml replacement
            string s =
                string.Format(
                    "var elem=document.getElementById('{1}'); if (elem) {{ var patch='{0}';ApplyPatch(elem,patch); }}",
                    JSUtil.fixString(content), id);
            s = s.Length + "|scriptBlock|ScriptContentNoTags|" + s + "|";
            return s;*/
            return string.Format("{0}|updatePanel|{1}|{2}|", content.Length, id, content);
        }

        /// <summary>
        /// Generates the patch string between the previous content and current rendered content
        /// </summary>
        /// <param name="content"></param>
        /// <param name="prev"></param>
        /// <returns></returns>
        private string GeneratePatch(string content, string prev)
        {
            var list = new List<UpdatePanelShrinkerPatch>();
            if (string.CompareOrdinal(content, prev) != 0)
            {
                int start = 0;
                int len = Math.Min(
                    Math.Min(content.Length, prev.Length)/10,
                    Math.Min(prev.Length - start - 1, content.Length - start - 1));
                while (len > 10)
                {
                    if (len > 0)
                        while ((start + len < content.Length) && (start + len < prev.Length) &&
                               (string.CompareOrdinal(content, start, prev, start, len) == 0))
                            start += len;
                    len = len/2;
                }

                int end = 0;
                len = Math.Min(Math.Min(content.Length - start, prev.Length - start)/10, end - start);
                while (len > 10)
                {
                    if (len > 0)
                        while ((content.Length - end - len > 0) && (prev.Length - end - len > 0) &&
                               (string.CompareOrdinal(content, content.Length - end - len, prev, prev.Length - end - len,
                                                      len) == 0))
                            end += len;
                    len = len/2;
                }

                list.Add(
                    new UpdatePanelShrinkerPatch(start, start, prev.Length - end - start,
                                                 content.Length - end - start));

                for (int i = 0; i < MaxLevel; i++)
                {
                    int count = list.Count;
                    list = ProcessPatchList(list, prev, content);
                    if (list.Count == count) break;
                }
            }
            return GetStringPatch(list, content);
        }

        /// <summary>
        /// Turns the patch list to the patch string
        /// </summary>
        /// <param name="list"></param>
        /// <param name="content"></param>
        /// <returns></returns>
        private static string GetStringPatch(IList<UpdatePanelShrinkerPatch> list, string content)
        {
            Debug.WriteLine("Patch fragments: " + list.Count);
            var sb = new StringBuilder();
            int contentLength = content.Length;
            sb.AppendFormat("pat|{0}|", contentLength);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                UpdatePanelShrinkerPatch p = list[i];
                sb.AppendFormat("{0}|{1}|{2}|{3}|", p.IndexPrevious, p.LengthPrevious, p.LengthCurrent,
                                (content.Substring(p.IndexCurrent, p.LengthCurrent)));
            }
            Debug.Write(
                string.Format("Shrinkage: from {0} to {1} = {2:P}\r\n", content.Length, sb.Length,
                              ((double) sb.Length)/content.Length));
            return sb.ToString();
        }

        /// <summary>
        /// Tries to refine the patch list
        /// </summary>
        /// <param name="list"></param>
        /// <param name="prev"></param>
        /// <param name="content"></param>
        /// <returns></returns>
        private List<UpdatePanelShrinkerPatch> ProcessPatchList(IList<UpdatePanelShrinkerPatch> list, string prev,
                                                                string content)
        {
            var newList = new List<UpdatePanelShrinkerPatch>();
            foreach (UpdatePanelShrinkerPatch p in list)
            {
                int minLength = Math.Min(p.LengthPrevious, p.LengthCurrent);
                if (minLength < MinLength)
                {
                    newList.Add(p);
                    continue;
                }

                int searchIndexPrevious = -1;
                int searchIndexCurrent = 0;
                int length = SearchLength;
                for (int c = 0; (c < TryCount) && (searchIndexPrevious == -1); c++)
                {
                    searchIndexCurrent = _rnd.Next(p.IndexCurrent, p.IndexCurrent + p.LengthCurrent - length);
                    string search = content.Substring(searchIndexCurrent, length);
                    searchIndexPrevious = prev.IndexOf(search, p.IndexPrevious, p.LengthPrevious - length,
                                                       StringComparison.Ordinal);
                    if (searchIndexPrevious <= -1) continue;
                    while (searchIndexCurrent > searchIndexPrevious)
                    {
                        int
                            f2 =
                                prev.IndexOf(search, searchIndexPrevious + 1,
                                             Math.Min(2*(searchIndexCurrent - searchIndexPrevious),
                                                      p.LengthPrevious - length -
                                                      (searchIndexPrevious - p.IndexPrevious)), StringComparison.Ordinal);
                        if (f2 > -1) searchIndexPrevious = f2;
                        else break;
                    }
                }
                if (searchIndexPrevious == -1)
                {
                    newList.Add(p);
                    continue;
                }
                int len = Math.Min(
                    Math.Min(p.LengthCurrent, p.LengthPrevious)/10,
                    Math.Min(p.IndexPrevious + p.LengthPrevious - searchIndexPrevious - length - 1,
                             p.IndexCurrent + p.LengthCurrent - searchIndexCurrent - length - 1));
                while (len > 10)
                {
                    if (len > 0)
                        while ((searchIndexCurrent + length + len < p.IndexCurrent + p.LengthCurrent) &&
                               (searchIndexPrevious + length + len < p.IndexPrevious + p.LengthPrevious)
                               &&
                               (string.CompareOrdinal(content, searchIndexCurrent + length, prev,
                                                      searchIndexPrevious + length, len) == 0))
                            length += len;
                    len = len/2;
                }

                len = Math.Min(
                    Math.Min(p.LengthCurrent - length, p.LengthPrevious - length)/10
                    ,
                    Math.Min(searchIndexPrevious - p.IndexPrevious, searchIndexCurrent - p.IndexCurrent)
                    );
                while (len > 10)
                {
                    if (len > 0)
                        while ((searchIndexCurrent - len > p.IndexCurrent) &&
                               (searchIndexPrevious - len > p.IndexPrevious)
                               &&
                               (string.CompareOrdinal(content, searchIndexCurrent - len, prev, searchIndexPrevious - len,
                                                      len) == 0)
                            )
                        {
                            length += len;
                            searchIndexPrevious -= len;
                            searchIndexCurrent -= len;
                        }
                    len = len/2;
                }
                var p1 =
                    new UpdatePanelShrinkerPatch(p.IndexPrevious, p.IndexCurrent, searchIndexPrevious - p.IndexPrevious,
                                                 searchIndexCurrent - p.IndexCurrent);
                var p2 =
                    new UpdatePanelShrinkerPatch(searchIndexPrevious + length, searchIndexCurrent + length,
                                                 p.IndexPrevious + p.LengthPrevious - (searchIndexPrevious + length),
                                                 p.IndexCurrent + p.LengthCurrent - (searchIndexCurrent + length));
                if (p1.LengthPrevious + p1.LengthCurrent > 0) newList.Add(p1);
                if (p2.LengthPrevious + p2.LengthCurrent > 0) newList.Add(p2);
            }
            return newList;
        }
    }
}