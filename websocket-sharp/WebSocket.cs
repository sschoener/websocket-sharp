#region License
/*
 * WebSocket.cs
 *
 * A C# implementation of the WebSocket interface.
 *
 * This code is derived from WebSocket.java
 * (http://github.com/adamac/Java-WebSocket-client).
 *
 * The MIT License
 *
 * Copyright (c) 2009 Adam MacBeth
 * Copyright (c) 2010-2016 sta.blockhead
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */
#endregion

#region Contributors
/*
 * Contributors:
 * - Frank Razenberg <frank@zzattack.org>
 * - David Wood <dpwood@gmail.com>
 * - Liryna <liryna.stark@gmail.com>
 */
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using WebSocketSharp.Net;
using WebSocketSharp.Net.WebSockets;

namespace WebSocketSharp
{
  /// <summary>
  /// Implements the WebSocket interface.
  /// </summary>
  /// <remarks>
  /// The WebSocket class provides a set of methods and properties for two-way communication using
  /// the WebSocket protocol (<see href="http://tools.ietf.org/html/rfc6455">RFC 6455</see>).
  /// </remarks>
  public class WebSocket : IDisposable
  {
    #region Private Fields

    private AuthenticationChallenge        _authChallenge;
    private string                         _base64Key;
    private bool                           _client;
    private Action                         _closeContext;
    private CompressionMethod              _compression;
    private WebSocketContext               _context;
    private CookieCollection               _cookies;
    private NetworkCredential              _credentials;
    private bool                           _emitOnPing;
    private bool                           _enableRedirection;
    private string                         _extensions;
    private bool                           _extensionsRequested;
    private object                         _forMessageEventQueue;
    private object                         _forPing;
    private object                         _forSend;
    private object                         _forState;
    private MemoryStream                   _fragmentsBuffer;
    private bool                           _fragmentsCompressed;
    private Opcode                         _fragmentsOpcode;
    private const string                   _guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private Func<WebSocketContext, string> _handshakeRequestChecker;
    private bool                           _ignoreExtensions;
    private bool                           _inContinuation;
    private volatile bool                  _inMessage;
    private volatile Logger                _logger;
    private static readonly int            _maxRetryCountForConnect;
    private Action<MessageEventArgs>       _message;
    private Queue<MessageEventArgs>        _messageEventQueue;
    private uint                           _nonceCount;
    private string                         _origin;
    private ManualResetEvent               _pongReceived;
    private bool                           _preAuth;
    private string                         _protocol;
    private string[]                       _protocols;
    private bool                           _protocolsRequested;
    private NetworkCredential              _proxyCredentials;
    private Uri                            _proxyUri;
    private volatile WebSocketState        _readyState;
    private ManualResetEvent               _receivingExited;
    private int                            _retryCountForConnect;
    private bool                           _secure;
    private ClientSslConfiguration         _sslConfig;
    private Stream                         _stream;
    private TcpClient                      _tcpClient;
    private Uri                            _uri;
    private const string                   _version = "13";
    private TimeSpan                       _waitTime;

    #endregion

    #region Internal Fields

    /// <summary>
    /// Represents the empty array of <see cref="byte"/> used internally.
    /// </summary>
    internal static readonly byte[] EmptyBytes;

    /// <summary>
    /// Represents the length used to determine whether the data should be fragmented in sending.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   The data will be fragmented if that length is greater than the value of this field.
    ///   </para>
    ///   <para>
    ///   If you would like to change the value, you must set it to a value between <c>125</c> and
    ///   <c>Int32.MaxValue - 14</c> inclusive.
    ///   </para>
    /// </remarks>
    public static int FragmentLength;

    /// <summary>
    /// Represents the random number generator used internally.
    /// </summary>
    internal static readonly RandomNumberGenerator RandomNumber;

    #endregion

    #region Static Constructor

    static WebSocket ()
    {
      _maxRetryCountForConnect = 10;
      EmptyBytes = new byte[0];
      FragmentLength = 1016;
      RandomNumber = new RNGCryptoServiceProvider ();
    }

    #endregion

    #region Internal Constructors

    // As server
    internal WebSocket (HttpListenerWebSocketContext context, string protocol)
    {
      _context = context;
      _protocol = protocol;

      _closeContext = context.Close;
      _logger = context.Log;
      _message = messages;
      _secure = context.IsSecureConnection;
      _stream = context.Stream;
      _waitTime = TimeSpan.FromSeconds (1);

      init ();
    }

    // As server
    internal WebSocket (TcpListenerWebSocketContext context, string protocol)
    {
      _context = context;
      _protocol = protocol;

      _closeContext = context.Close;
      _logger = context.Log;
      _message = messages;
      _secure = context.IsSecureConnection;
      _stream = context.Stream;
      _waitTime = TimeSpan.FromSeconds (1);

      init ();
    }

    #endregion

    #region Public Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocket"/> class with
    /// <paramref name="url"/> and <paramref name="protocols"/>.
    /// </summary>
    /// <param name="url">
    /// A <see cref="string"/> that specifies the URL of the WebSocket
    /// server to connect.
    /// </param>
    /// <param name="protocols">
    ///   <para>
    ///   An array of <see cref="string"/> that specifies the names of
    ///   the subprotocols if necessary.
    ///   </para>
    ///   <para>
    ///   Each value of the array must be a token defined in
    ///   <see href="http://tools.ietf.org/html/rfc2616#section-2.2">
    ///   RFC 2616</see>.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="url"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="url"/> is an empty string.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="url"/> is an invalid WebSocket URL string.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="protocols"/> contains a value that is not a token.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="protocols"/> contains a value twice.
    ///   </para>
    /// </exception>
    public WebSocket (string url, params string[] protocols)
    {
      if (url == null)
        throw new ArgumentNullException ("url");

      if (url.Length == 0)
        throw new ArgumentException ("An empty string.", "url");

      string msg;
      if (!url.TryCreateWebSocketUri (out _uri, out msg))
        throw new ArgumentException (msg, "url");

      if (protocols != null && protocols.Length > 0) {
        if (!checkProtocols (protocols, out msg))
          throw new ArgumentException (msg, "protocols");

        _protocols = protocols;
      }

      _base64Key = CreateBase64Key ();
      _client = true;
      _logger = new Logger ();
      _message = messagec;
      _secure = _uri.Scheme == "wss";
      _waitTime = TimeSpan.FromSeconds (5);

      init ();
    }

    #endregion

    #region Internal Properties

    internal CookieCollection CookieCollection {
      get {
        return _cookies;
      }
    }

    // As server
    internal Func<WebSocketContext, string> CustomHandshakeRequestChecker {
      get {
        return _handshakeRequestChecker;
      }

      set {
        _handshakeRequestChecker = value;
      }
    }

    internal bool HasMessage {
      get {
        lock (_forMessageEventQueue)
          return _messageEventQueue.Count > 0;
      }
    }

    // As server
    internal bool IgnoreExtensions {
      get {
        return _ignoreExtensions;
      }

      set {
        _ignoreExtensions = value;
      }
    }

    internal bool IsConnected {
      get {
        return _readyState == WebSocketState.Open || _readyState == WebSocketState.Closing;
      }
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the compression method used to compress a message.
    /// </summary>
    /// <remarks>
    /// The set operation does nothing if the connection has already been
    /// established or it is closing.
    /// </remarks>
    /// <value>
    ///   <para>
    ///   One of the <see cref="CompressionMethod"/> enum values.
    ///   </para>
    ///   <para>
    ///   It represents the compression method used to compress a message.
    ///   </para>
    ///   <para>
    ///   The default value is <see cref="CompressionMethod.None"/>.
    ///   </para>
    /// </value>
    /// <exception cref="InvalidOperationException">
    /// The set operation cannot be used by servers.
    /// </exception>
    public CompressionMethod Compression {
      get {
        return _compression;
      }

      set {
        string msg = null;

        if (!_client) {
          msg = "The set operation cannot be used by servers.";
          throw new InvalidOperationException (msg);
        }

        if (!canSet (out msg)) {
          _logger.Warn (msg);
          return;
        }

        lock (_forState) {
          if (!canSet (out msg)) {
            _logger.Warn (msg);
            return;
          }

          _compression = value;
        }
      }
    }

    /// <summary>
    /// Gets the HTTP cookies included in the WebSocket handshake request and response.
    /// </summary>
    /// <value>
    /// An <see cref="T:System.Collections.Generic.IEnumerable{WebSocketSharp.Net.Cookie}"/>
    /// instance that provides an enumerator which supports the iteration over the collection of
    /// the cookies.
    /// </value>
    public IEnumerable<Cookie> Cookies {
      get {
        lock (_cookies.SyncRoot)
          foreach (Cookie cookie in _cookies)
            yield return cookie;
      }
    }

    /// <summary>
    /// Gets the credentials for the HTTP authentication (Basic/Digest).
    /// </summary>
    /// <value>
    /// A <see cref="NetworkCredential"/> that represents the credentials for
    /// the authentication. The default value is <see langword="null"/>.
    /// </value>
    public NetworkCredential Credentials {
      get {
        return _credentials;
      }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the <see cref="WebSocket"/> emits
    /// a <see cref="OnMessage"/> event when receives a ping.
    /// </summary>
    /// <value>
    /// <c>true</c> if the <see cref="WebSocket"/> emits a <see cref="OnMessage"/> event
    /// when receives a ping; otherwise, <c>false</c>. The default value is <c>false</c>.
    /// </value>
    public bool EmitOnPing {
      get {
        return _emitOnPing;
      }

      set {
        _emitOnPing = value;
      }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the URL redirection for
    /// the handshake request is allowed.
    /// </summary>
    /// <remarks>
    /// The set operation does nothing if the connection has already been
    /// established or it is closing.
    /// </remarks>
    /// <value>
    ///   <para>
    ///   <c>true</c> if the URL redirection for the handshake request is
    ///   allowed; otherwise, <c>false</c>.
    ///   </para>
    ///   <para>
    ///   The default value is <c>false</c>.
    ///   </para>
    /// </value>
    /// <exception cref="InvalidOperationException">
    /// The set operation cannot be used by servers.
    /// </exception>
    public bool EnableRedirection {
      get {
        return _enableRedirection;
      }

      set {
        string msg = null;

        if (!_client) {
          msg = "The set operation cannot be used by servers.";
          throw new InvalidOperationException (msg);
        }

        if (!canSet (out msg)) {
          _logger.Warn (msg);
          return;
        }

        lock (_forState) {
          if (!canSet (out msg)) {
            _logger.Warn (msg);
            return;
          }

          _enableRedirection = value;
        }
      }
    }

    /// <summary>
    /// Gets the WebSocket extensions selected by the server.
    /// </summary>
    /// <value>
    /// A <see cref="string"/> that represents the extensions if any.
    /// The default value is <see cref="String.Empty"/>.
    /// </value>
    public string Extensions {
      get {
        return _extensions ?? String.Empty;
      }
    }

    /// <summary>
    /// Gets a value indicating whether the WebSocket connection is alive.
    /// </summary>
    /// <value>
    /// <c>true</c> if the connection is alive; otherwise, <c>false</c>.
    /// </value>
    public bool IsAlive {
      get {
        return ping (EmptyBytes);
      }
    }

    /// <summary>
    /// Gets a value indicating whether the WebSocket connection is secure.
    /// </summary>
    /// <value>
    /// <c>true</c> if the connection is secure; otherwise, <c>false</c>.
    /// </value>
    public bool IsSecure {
      get {
        return _secure;
      }
    }

    /// <summary>
    /// Gets the logging functions.
    /// </summary>
    /// <remarks>
    /// The default logging level is <see cref="LogLevel.Error"/>. If you would like to change it,
    /// you should set this <c>Log.Level</c> property to any of the <see cref="LogLevel"/> enum
    /// values.
    /// </remarks>
    /// <value>
    /// A <see cref="Logger"/> that provides the logging functions.
    /// </value>
    public Logger Log {
      get {
        return _logger;
      }

      internal set {
        _logger = value;
      }
    }

    /// <summary>
    /// Gets or sets the value of the HTTP Origin header to send with
    /// the handshake request.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   The HTTP Origin header is defined in
    ///   <see href="http://tools.ietf.org/html/rfc6454#section-7">
    ///   Section 7 of RFC 6454</see>.
    ///   </para>
    ///   <para>
    ///   This instance sends the Origin header if this property has any.
    ///   </para>
    ///   <para>
    ///   The set operation does nothing if the connection has already been
    ///   established or it is closing.
    ///   </para>
    /// </remarks>
    /// <value>
    ///   <para>
    ///   A <see cref="string"/> that represents the value of the Origin
    ///   header to send.
    ///   </para>
    ///   <para>
    ///   The syntax is &lt;scheme&gt;://&lt;host&gt;[:&lt;port&gt;].
    ///   </para>
    ///   <para>
    ///   The default value is <see langword="null"/>.
    ///   </para>
    /// </value>
    /// <exception cref="InvalidOperationException">
    /// The set operation is not available if this instance is not a client.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   The value specified for a set operation is not an absolute URI string.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The value specified for a set operation includes the path segments.
    ///   </para>
    /// </exception>
    public string Origin {
      get {
        return _origin;
      }

      set {
        string msg = null;

        if (!_client) {
          msg = "This instance is not a client.";
          throw new InvalidOperationException (msg);
        }

        if (!value.IsNullOrEmpty ()) {
          Uri uri;
          if (!Uri.TryCreate (value, UriKind.Absolute, out uri)) {
            msg = "Not an absolute URI string.";
            throw new ArgumentException (msg, value);
          }

          if (uri.Segments.Length > 1) {
            msg = "It includes the path segments.";
            throw new ArgumentException (msg, value);
          }
        }

        if (!canSet (out msg)) {
          _logger.Warn (msg);
          return;
        }

        lock (_forState) {
          if (!canSet (out msg)) {
            _logger.Warn (msg);
            return;
          }

          _origin = !value.IsNullOrEmpty () ? value.TrimEnd ('/') : value;
        }
      }
    }

    /// <summary>
    /// Gets the WebSocket subprotocol selected by the server.
    /// </summary>
    /// <value>
    /// A <see cref="string"/> that represents the subprotocol if any.
    /// The default value is <see cref="String.Empty"/>.
    /// </value>
    public string Protocol {
      get {
        return _protocol ?? String.Empty;
      }

      internal set {
        _protocol = value;
      }
    }

    /// <summary>
    /// Gets the state of the WebSocket connection.
    /// </summary>
    /// <value>
    /// One of the <see cref="WebSocketState"/> enum values that indicates
    /// the current state of the connection. The default value is
    /// <see cref="WebSocketState.Connecting"/>.
    /// </value>
    public WebSocketState ReadyState {
      get {
        return _readyState;
      }
    }

    /// <summary>
    /// Gets the configuration for secure connection.
    /// </summary>
    /// <remarks>
    /// This configuration will be referenced when attempts to connect,
    /// so it must be configured before any connect method is called.
    /// </remarks>
    /// <value>
    /// A <see cref="ClientSslConfiguration"/> that represents
    /// the configuration used to establish a secure connection.
    /// </value>
    /// <exception cref="InvalidOperationException">
    ///   <para>
    ///   This instance is not a client.
    ///   </para>
    ///   <para>
    ///   This instance does not use a secure connection.
    ///   </para>
    /// </exception>
    public ClientSslConfiguration SslConfiguration {
      get {
        if (!_client) {
          var msg = "This instance is not a client.";
          throw new InvalidOperationException (msg);
        }

        if (!_secure) {
          var msg = "This instance does not use a secure connection.";
          throw new InvalidOperationException (msg);
        }

        if (_sslConfig == null)
          _sslConfig = new ClientSslConfiguration (_uri.DnsSafeHost);

        return _sslConfig;
      }
    }

    /// <summary>
    /// Gets the WebSocket URL used to connect, or accepted.
    /// </summary>
    /// <value>
    /// A <see cref="Uri"/> that represents the URL used to connect, or accepted.
    /// </value>
    public Uri Url {
      get {
        return _client ? _uri : _context.RequestUri;
      }
    }

    /// <summary>
    /// Gets or sets the time to wait for the response to the ping or close.
    /// </summary>
    /// <remarks>
    /// The set operation does nothing if the connection has already been
    /// established or it is closing.
    /// </remarks>
    /// <value>
    ///   <para>
    ///   A <see cref="TimeSpan"/> to wait for the response.
    ///   </para>
    ///   <para>
    ///   The default value is the same as 5 seconds if the instance is
    ///   a client.
    ///   </para>
    /// </value>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value specified for a set operation is zero or less.
    /// </exception>
    public TimeSpan WaitTime {
      get {
        return _waitTime;
      }

      set {
        if (value <= TimeSpan.Zero)
          throw new ArgumentOutOfRangeException ("value", "Zero or less.");

        string msg;
        if (!canSet (out msg)) {
          _logger.Warn (msg);
          return;
        }

        lock (_forState) {
          if (!canSet (out msg)) {
            _logger.Warn (msg);
            return;
          }

          _waitTime = value;
        }
      }
    }

    #endregion

    #region Public Events

    /// <summary>
    /// Occurs when the WebSocket connection has been closed.
    /// </summary>
    public event EventHandler<CloseEventArgs> OnClose;

    /// <summary>
    /// Occurs when the <see cref="WebSocket"/> gets an error.
    /// </summary>
    public event EventHandler<ErrorEventArgs> OnError;

    /// <summary>
    /// Occurs when the <see cref="WebSocket"/> receives a message.
    /// </summary>
    public event EventHandler<MessageEventArgs> OnMessage;

    /// <summary>
    /// Occurs when the WebSocket connection has been established.
    /// </summary>
    public event EventHandler OnOpen;

    #endregion

    #region Private Methods

    // As server
    private bool accept ()
    {
      lock (_forState) {
        string msg;
        if (!checkIfAvailable (true, false, false, false, out msg)) {
          _logger.Error (msg);
          error ("An error has occurred in accepting.", null);

          return false;
        }

        try {
          if (!acceptHandshake ())
            return false;

          _readyState = WebSocketState.Open;
        }
        catch (Exception ex) {
          _logger.Fatal (ex.ToString ());
          fatal ("An exception has occurred while accepting.", ex);

          return false;
        }

        return true;
      }
    }

    // As server
    private bool acceptHandshake ()
    {
      _logger.Debug (String.Format ("A request from {0}:\n{1}", _context.UserEndPoint, _context));

      string msg;
      if (!checkHandshakeRequest (_context, out msg)) {
        sendHttpResponse (createHandshakeFailureResponse (HttpStatusCode.BadRequest));

        _logger.Fatal (msg);
        fatal ("An error has occurred while accepting.", CloseStatusCode.ProtocolError);

        return false;
      }

      if (!customCheckHandshakeRequest (_context, out msg)) {
        sendHttpResponse (createHandshakeFailureResponse (HttpStatusCode.BadRequest));

        _logger.Fatal (msg);
        fatal ("An error has occurred while accepting.", CloseStatusCode.PolicyViolation);

        return false;
      }

      _base64Key = _context.Headers["Sec-WebSocket-Key"];

      if (_protocol != null)
        processSecWebSocketProtocolHeader (_context.SecWebSocketProtocols);

      if (!_ignoreExtensions)
        processSecWebSocketExtensionsClientHeader (_context.Headers["Sec-WebSocket-Extensions"]);

      return sendHttpResponse (createHandshakeResponse ());
    }

    private bool canSet (out string message)
    {
      message = null;

      if (_readyState == WebSocketState.Open) {
        message = "The connection has already been established.";
        return false;
      }

      if (_readyState == WebSocketState.Closing) {
        message = "The connection is closing.";
        return false;
      }

      return true;
    }

    // As server
    private bool checkHandshakeRequest (WebSocketContext context, out string message)
    {
      message = null;

      if (context.RequestUri == null) {
        message = "Specifies an invalid Request-URI.";
        return false;
      }

      if (!context.IsWebSocketRequest) {
        message = "Not a WebSocket handshake request.";
        return false;
      }

      var headers = context.Headers;
      if (!validateSecWebSocketKeyHeader (headers["Sec-WebSocket-Key"])) {
        message = "Includes no Sec-WebSocket-Key header, or it has an invalid value.";
        return false;
      }

      if (!validateSecWebSocketVersionClientHeader (headers["Sec-WebSocket-Version"])) {
        message = "Includes no Sec-WebSocket-Version header, or it has an invalid value.";
        return false;
      }

      if (!validateSecWebSocketProtocolClientHeader (headers["Sec-WebSocket-Protocol"])) {
        message = "Includes an invalid Sec-WebSocket-Protocol header.";
        return false;
      }

      if (!_ignoreExtensions
          && !validateSecWebSocketExtensionsClientHeader (headers["Sec-WebSocket-Extensions"])
      ) {
        message = "Includes an invalid Sec-WebSocket-Extensions header.";
        return false;
      }

      return true;
    }

    // As client
    private bool checkHandshakeResponse (HttpResponse response, out string message)
    {
      message = null;

      if (response.IsRedirect) {
        message = "Indicates the redirection.";
        return false;
      }

      if (response.IsUnauthorized) {
        message = "Requires the authentication.";
        return false;
      }

      if (!response.IsWebSocketResponse) {
        message = "Not a WebSocket handshake response.";
        return false;
      }

      var headers = response.Headers;
      if (!validateSecWebSocketAcceptHeader (headers["Sec-WebSocket-Accept"])) {
        message = "Includes no Sec-WebSocket-Accept header, or it has an invalid value.";
        return false;
      }

      if (!validateSecWebSocketProtocolServerHeader (headers["Sec-WebSocket-Protocol"])) {
        message = "Includes no Sec-WebSocket-Protocol header, or it has an invalid value.";
        return false;
      }

      if (!validateSecWebSocketExtensionsServerHeader (headers["Sec-WebSocket-Extensions"])) {
        message = "Includes an invalid Sec-WebSocket-Extensions header.";
        return false;
      }

      if (!validateSecWebSocketVersionServerHeader (headers["Sec-WebSocket-Version"])) {
        message = "Includes an invalid Sec-WebSocket-Version header.";
        return false;
      }

      return true;
    }

    private bool checkIfAvailable (
      bool connecting, bool open, bool closing, bool closed, out string message
    )
    {
      message = null;

      if (!connecting && _readyState == WebSocketState.Connecting) {
        message = "This operation is not available in: connecting";
        return false;
      }

      if (!open && _readyState == WebSocketState.Open) {
        message = "This operation is not available in: open";
        return false;
      }

      if (!closing && _readyState == WebSocketState.Closing) {
        message = "This operation is not available in: closing";
        return false;
      }

      if (!closed && _readyState == WebSocketState.Closed) {
        message = "This operation is not available in: closed";
        return false;
      }

      return true;
    }

    private bool checkIfAvailable (
      bool client,
      bool server,
      bool connecting,
      bool open,
      bool closing,
      bool closed,
      out string message
    )
    {
      message = null;

      if (!client && _client) {
        message = "This operation is not available in: client";
        return false;
      }

      if (!server && !_client) {
        message = "This operation is not available in: server";
        return false;
      }

      return checkIfAvailable (connecting, open, closing, closed, out message);
    }

    private static bool checkParametersForSetCredentials (
      string username, string password, out string message
    )
    {
      message = null;

      if (username.IsNullOrEmpty ())
        return true;

      if (username.Contains (':') || !username.IsText ()) {
        message = "'username' contains an invalid character.";
        return false;
      }

      if (password.IsNullOrEmpty ())
        return true;

      if (!password.IsText ()) {
        message = "'password' contains an invalid character.";
        return false;
      }

      return true;
    }

    private static bool checkParametersForSetProxy (
      string url, string username, string password, out string message
    )
    {
      message = null;

      if (url.IsNullOrEmpty ())
        return true;

      Uri uri;
      if (!Uri.TryCreate (url, UriKind.Absolute, out uri)
          || uri.Scheme != "http"
          || uri.Segments.Length > 1
      ) {
        message = "'url' is an invalid URL.";
        return false;
      }

      if (username.IsNullOrEmpty ())
        return true;

      if (username.Contains (':') || !username.IsText ()) {
        message = "'username' contains an invalid character.";
        return false;
      }

      if (password.IsNullOrEmpty ())
        return true;

      if (!password.IsText ()) {
        message = "'password' contains an invalid character.";
        return false;
      }

      return true;
    }

    private static bool checkProtocols (string[] protocols, out string message)
    {
      message = null;

      Func<string, bool> cond = protocol => protocol.IsNullOrEmpty ()
                                            || !protocol.IsToken ();

      if (protocols.Contains (cond)) {
        message = "It contains a value that is not a token.";
        return false;
      }

      if (protocols.ContainsTwice ()) {
        message = "It contains a value twice.";
        return false;
      }

      return true;
    }

    private bool checkReceivedFrame (WebSocketFrame frame, out string message)
    {
      message = null;

      var masked = frame.IsMasked;
      if (_client && masked) {
        message = "A frame from the server is masked.";
        return false;
      }

      if (!_client && !masked) {
        message = "A frame from a client is not masked.";
        return false;
      }

      if (_inContinuation && frame.IsData) {
        message = "A data frame has been received while receiving continuation frames.";
        return false;
      }

      if (frame.IsCompressed && _compression == CompressionMethod.None) {
        message = "A compressed frame has been received without any agreement for it.";
        return false;
      }

      if (frame.Rsv2 == Rsv.On) {
        message = "The RSV2 of a frame is non-zero without any negotiation for it.";
        return false;
      }

      if (frame.Rsv3 == Rsv.On) {
        message = "The RSV3 of a frame is non-zero without any negotiation for it.";
        return false;
      }

      return true;
    }

    private void close (ushort code, string reason)
    {
      if (_readyState == WebSocketState.Closing) {
        _logger.Info ("The closing is already in progress.");
        return;
      }

      if (_readyState == WebSocketState.Closed) {
        _logger.Info ("The connection has already been closed.");
        return;
      }

      if (code == 1005) { // == no status
        close (PayloadData.Empty, true, true, false);
        return;
      }

      var send = !code.IsReserved ();
      close (new PayloadData (code, reason), send, send, false);
    }

    private void close (
      PayloadData payloadData, bool send, bool receive, bool received
    )
    {
      lock (_forState) {
        if (_readyState == WebSocketState.Closing) {
          _logger.Info ("The closing is already in progress.");
          return;
        }

        if (_readyState == WebSocketState.Closed) {
          _logger.Info ("The connection has already been closed.");
          return;
        }

        send = send && _readyState == WebSocketState.Open;
        receive = send && receive;

        _readyState = WebSocketState.Closing;
      }

      _logger.Trace ("Begin closing the connection.");

      var res = closeHandshake (payloadData, send, receive, received);
      releaseResources ();

      _logger.Trace ("End closing the connection.");

      _readyState = WebSocketState.Closed;

      var e = new CloseEventArgs (payloadData);
      e.WasClean = res;

      try {
        OnClose.Emit (this, e);
      }
      catch (Exception ex) {
        _logger.Error (ex.ToString ());
        error ("An error has occurred during the OnClose event.", ex);
      }
    }

    private void closeAsync (ushort code, string reason)
    {
      if (_readyState == WebSocketState.Closing) {
        _logger.Info ("The closing is already in progress.");
        return;
      }

      if (_readyState == WebSocketState.Closed) {
        _logger.Info ("The connection has already been closed.");
        return;
      }

      if (code == 1005) { // == no status
        closeAsync (PayloadData.Empty, true, true, false);
        return;
      }

      var send = !code.IsReserved ();
      closeAsync (new PayloadData (code, reason), send, send, false);
    }

    private void closeAsync (
      PayloadData payloadData, bool send, bool receive, bool received
    )
    {
      System.Threading.Tasks.Task.Run(() => close(payloadData, send, receive, received));
    }

    private bool closeHandshake (byte[] frameAsBytes, bool receive, bool received)
    {
      var sent = frameAsBytes != null && sendBytes (frameAsBytes);

      var wait = !received && sent && receive && _receivingExited != null;
      if (wait)
        received = _receivingExited.WaitOne (_waitTime);

      var ret = sent && received;

      _logger.Debug (
        String.Format (
          "Was clean?: {0}\n  sent: {1}\n  received: {2}", ret, sent, received
        )
      );

      return ret;
    }

    private bool closeHandshake (
      PayloadData payloadData, bool send, bool receive, bool received
    )
    {
      var sent = false;
      if (send) {
        var frame = WebSocketFrame.CreateCloseFrame (payloadData, _client);
        sent = sendBytes (frame.ToArray ());

        if (_client)
          frame.Unmask ();
      }

      var wait = !received && sent && receive && _receivingExited != null;
      if (wait)
        received = _receivingExited.WaitOne (_waitTime);

      var ret = sent && received;

      _logger.Debug (
        String.Format (
          "Was clean?: {0}\n  sent: {1}\n  received: {2}", ret, sent, received
        )
      );

      return ret;
    }

    // As client
    private bool connect ()
    {
      lock (_forState) {
        string msg;
        if (!checkIfAvailable (true, false, false, true, out msg)) {
          _logger.Error (msg);
          error ("An error has occurred in connecting.", null);

          return false;
        }

        if (_retryCountForConnect > _maxRetryCountForConnect) {
          _retryCountForConnect = 0;
          _logger.Fatal ("A series of reconnecting has failed.");

          return false;
        }

        _readyState = WebSocketState.Connecting;

        try {
          doHandshake ();
        }
        catch (Exception ex) {
          _retryCountForConnect++;
          _logger.Fatal (ex.ToString ());
          fatal ("An exception has occurred while connecting.", ex);

          return false;
        }

        _retryCountForConnect = 1;
        _readyState = WebSocketState.Open;

        return true;
      }
    }

    // As client
    private string createExtensions ()
    {
      var buff = new StringBuilder (80);

      if (_compression != CompressionMethod.None) {
        var str = _compression.ToExtensionString (
          "server_no_context_takeover", "client_no_context_takeover");

        buff.AppendFormat ("{0}, ", str);
      }

      var len = buff.Length;
      if (len > 2) {
        buff.Length = len - 2;
        return buff.ToString ();
      }

      return null;
    }

    // As server
    private HttpResponse createHandshakeFailureResponse (HttpStatusCode code)
    {
      var ret = HttpResponse.CreateCloseResponse (code);
      ret.Headers["Sec-WebSocket-Version"] = _version;

      return ret;
    }

    // As client
    private HttpRequest createHandshakeRequest ()
    {
      var ret = HttpRequest.CreateWebSocketRequest (_uri);

      var headers = ret.Headers;
      if (!_origin.IsNullOrEmpty ())
        headers["Origin"] = _origin;

      headers["Sec-WebSocket-Key"] = _base64Key;

      _protocolsRequested = _protocols != null;
      if (_protocolsRequested)
        headers["Sec-WebSocket-Protocol"] = _protocols.ToString (", ");

      _extensionsRequested = _compression != CompressionMethod.None;
      if (_extensionsRequested)
        headers["Sec-WebSocket-Extensions"] = createExtensions ();

      headers["Sec-WebSocket-Version"] = _version;

      AuthenticationResponse authRes = null;
      if (_authChallenge != null && _credentials != null) {
        authRes = new AuthenticationResponse (_authChallenge, _credentials, _nonceCount);
        _nonceCount = authRes.NonceCount;
      }
      else if (_preAuth) {
        authRes = new AuthenticationResponse (_credentials);
      }

      if (authRes != null)
        headers["Authorization"] = authRes.ToString ();

      if (_cookies.Count > 0)
        ret.SetCookies (_cookies);

      return ret;
    }

    // As server
    private HttpResponse createHandshakeResponse ()
    {
      var ret = HttpResponse.CreateWebSocketResponse ();

      var headers = ret.Headers;
      headers["Sec-WebSocket-Accept"] = CreateResponseKey (_base64Key);

      if (_protocol != null)
        headers["Sec-WebSocket-Protocol"] = _protocol;

      if (_extensions != null)
        headers["Sec-WebSocket-Extensions"] = _extensions;

      if (_cookies.Count > 0)
        ret.SetCookies (_cookies);

      return ret;
    }

    // As server
    private bool customCheckHandshakeRequest (WebSocketContext context, out string message)
    {
      message = null;
      return _handshakeRequestChecker == null
             || (message = _handshakeRequestChecker (context)) == null;
    }

    private MessageEventArgs dequeueFromMessageEventQueue ()
    {
      lock (_forMessageEventQueue)
        return _messageEventQueue.Count > 0 ? _messageEventQueue.Dequeue () : null;
    }

    // As client
    private void doHandshake ()
    {
      setClientStream ();
      var res = sendHandshakeRequest ();

      string msg;
      if (!checkHandshakeResponse (res, out msg))
        throw new WebSocketException (CloseStatusCode.ProtocolError, msg);

      if (_protocolsRequested)
        _protocol = res.Headers["Sec-WebSocket-Protocol"];

      if (_extensionsRequested)
        processSecWebSocketExtensionsServerHeader (res.Headers["Sec-WebSocket-Extensions"]);

      processCookies (res.Cookies);
    }

    private void enqueueToMessageEventQueue (MessageEventArgs e)
    {
      lock (_forMessageEventQueue)
        _messageEventQueue.Enqueue (e);
    }

    private void error (string message, Exception exception)
    {
      try {
        OnError.Emit (this, new ErrorEventArgs (message, exception));
      }
      catch (Exception ex) {
        _logger.Error (ex.ToString ());
      }
    }

    private void fatal (string message, Exception exception)
    {
      var code = exception is WebSocketException
                 ? ((WebSocketException) exception).Code
                 : CloseStatusCode.Abnormal;

      fatal (message, (ushort) code);
    }

    private void fatal (string message, ushort code)
    {
      var payload = new PayloadData (code, message);
      close (payload, !code.IsReserved (), false, false);
    }

    private void fatal (string message, CloseStatusCode code)
    {
      fatal (message, (ushort) code);
    }

    private void init ()
    {
      _compression = CompressionMethod.None;
      _cookies = new CookieCollection ();
      _forPing = new object ();
      _forSend = new object ();
      _forState = new object ();
      _messageEventQueue = new Queue<MessageEventArgs> ();
      _forMessageEventQueue = ((ICollection) _messageEventQueue).SyncRoot;
      _readyState = WebSocketState.Connecting;
    }

    private void message ()
    {
      MessageEventArgs e = null;
      lock (_forMessageEventQueue) {
        if (_inMessage || _messageEventQueue.Count == 0 || _readyState != WebSocketState.Open)
          return;

        _inMessage = true;
        e = _messageEventQueue.Dequeue ();
      }

      _message (e);
    }

    private void messagec (MessageEventArgs e)
    {
      do {
        try {
          OnMessage.Emit (this, e);
        }
        catch (Exception ex) {
          _logger.Error (ex.ToString ());
          error ("An error has occurred during an OnMessage event.", ex);
        }

        lock (_forMessageEventQueue) {
          if (_messageEventQueue.Count == 0 || _readyState != WebSocketState.Open) {
            _inMessage = false;
            break;
          }

          e = _messageEventQueue.Dequeue ();
        }
      }
      while (true);
    }

    private void messages (MessageEventArgs e)
    {
      try {
        OnMessage.Emit (this, e);
      }
      catch (Exception ex) {
        _logger.Error (ex.ToString ());
        error ("An error has occurred during an OnMessage event.", ex);
      }

      lock (_forMessageEventQueue) {
        if (_messageEventQueue.Count == 0 || _readyState != WebSocketState.Open) {
          _inMessage = false;
          return;
        }

        e = _messageEventQueue.Dequeue ();
      }

      ThreadPool.QueueUserWorkItem (state => messages (e));
    }

    private void open ()
    {
      _inMessage = true;
      startReceiving ();
      try {
        OnOpen.Emit (this, EventArgs.Empty);
      }
      catch (Exception ex) {
        _logger.Error (ex.ToString ());
        error ("An error has occurred during the OnOpen event.", ex);
      }

      MessageEventArgs e = null;
      lock (_forMessageEventQueue) {
        if (_messageEventQueue.Count == 0 || _readyState != WebSocketState.Open) {
          _inMessage = false;
          return;
        }

        e = _messageEventQueue.Dequeue ();
      }
      var msg = _message;
      System.Threading.Tasks.Task.Run(() => msg(e));
    }

    private bool ping (byte[] data)
    {
      if (_readyState != WebSocketState.Open)
        return false;

      var pongReceived = _pongReceived;
      if (pongReceived == null)
        return false;

      lock (_forPing) {
        try {
          pongReceived.Reset ();
          if (!send (Fin.Final, Opcode.Ping, data, false))
            return false;

          return pongReceived.WaitOne (_waitTime);
        }
        catch (ObjectDisposedException) {
          return false;
        }
      }
    }

    private bool processCloseFrame (WebSocketFrame frame)
    {
      var payload = frame.PayloadData;
      close (payload, !payload.HasReservedCode, false, true);

      return false;
    }

    // As client
    private void processCookies (CookieCollection cookies)
    {
      if (cookies.Count == 0)
        return;

      _cookies.SetOrRemove (cookies);
    }

    private bool processDataFrame (WebSocketFrame frame)
    {
      enqueueToMessageEventQueue (
        frame.IsCompressed
        ? new MessageEventArgs (
            frame.Opcode, frame.PayloadData.ApplicationData.Decompress (_compression))
        : new MessageEventArgs (frame));

      return true;
    }

    private bool processFragmentFrame (WebSocketFrame frame)
    {
      if (!_inContinuation) {
        // Must process first fragment.
        if (frame.IsContinuation)
          return true;

        _fragmentsOpcode = frame.Opcode;
        _fragmentsCompressed = frame.IsCompressed;
        _fragmentsBuffer = new MemoryStream ();
        _inContinuation = true;
      }

      _fragmentsBuffer.WriteBytes (frame.PayloadData.ApplicationData, 1024);
      if (frame.IsFinal) {
        using (_fragmentsBuffer) {
          var data = _fragmentsCompressed
                     ? _fragmentsBuffer.DecompressToArray (_compression)
                     : _fragmentsBuffer.ToArray ();

          enqueueToMessageEventQueue (new MessageEventArgs (_fragmentsOpcode, data));
        }

        _fragmentsBuffer = null;
        _inContinuation = false;
      }

      return true;
    }

    private bool processPingFrame (WebSocketFrame frame)
    {
      _logger.Trace ("A ping was received.");

      var pong = WebSocketFrame.CreatePongFrame (frame.PayloadData, _client);

      lock (_forState) {
        if (_readyState != WebSocketState.Open) {
          _logger.Error ("The connection is closing.");
          return true;
        }

        if (!sendBytes (pong.ToArray ()))
          return false;
      }

      _logger.Trace ("A pong to this ping has been sent.");

      if (_emitOnPing) {
        if (_client)
          pong.Unmask ();

        enqueueToMessageEventQueue (new MessageEventArgs (frame));
      }

      return true;
    }

    private bool processPongFrame (WebSocketFrame frame)
    {
      _logger.Trace ("A pong was received.");

      try {
        _pongReceived.Set ();
      }
      catch (NullReferenceException ex) {
        _logger.Error (ex.Message);
        _logger.Debug (ex.ToString ());

        return false;
      }
      catch (ObjectDisposedException ex) {
        _logger.Error (ex.Message);
        _logger.Debug (ex.ToString ());

        return false;
      }

      _logger.Trace ("It has been signaled.");

      return true;
    }

    private bool processReceivedFrame (WebSocketFrame frame)
    {
      string msg;
      if (!checkReceivedFrame (frame, out msg))
        throw new WebSocketException (CloseStatusCode.ProtocolError, msg);

      frame.Unmask ();
      return frame.IsFragment
             ? processFragmentFrame (frame)
             : frame.IsData
               ? processDataFrame (frame)
               : frame.IsPing
                 ? processPingFrame (frame)
                 : frame.IsPong
                   ? processPongFrame (frame)
                   : frame.IsClose
                     ? processCloseFrame (frame)
                     : processUnsupportedFrame (frame);
    }

    // As server
    private void processSecWebSocketExtensionsClientHeader (string value)
    {
      if (value == null)
        return;

      var buff = new StringBuilder (80);

      var comp = false;
      foreach (var e in value.SplitHeaderValue (',')) {
        var ext = e.Trim ();
        if (!comp && ext.IsCompressionExtension (CompressionMethod.Deflate)) {
          _compression = CompressionMethod.Deflate;
          buff.AppendFormat (
            "{0}, ",
            _compression.ToExtensionString (
              "client_no_context_takeover", "server_no_context_takeover"
            )
          );

          comp = true;
        }
      }

      var len = buff.Length;
      if (len > 2) {
        buff.Length = len - 2;
        _extensions = buff.ToString ();
      }
    }

    // As client
    private void processSecWebSocketExtensionsServerHeader (string value)
    {
      if (value == null) {
        _compression = CompressionMethod.None;
        return;
      }

      _extensions = value;
    }

    // As server
    private void processSecWebSocketProtocolHeader (IEnumerable<string> values)
    {
      if (values.Contains (p => p == _protocol))
        return;

      _protocol = null;
    }

    private bool processUnsupportedFrame (WebSocketFrame frame)
    {
      _logger.Fatal ("An unsupported frame:" + frame.PrintToString (false));
      fatal ("There is no way to handle it.", CloseStatusCode.PolicyViolation);

      return false;
    }

    // As client
    private void releaseClientResources ()
    {
      if (_stream != null) {
        _stream.Dispose ();
        _stream = null;
      }

      if (_tcpClient != null) {
        _tcpClient.Close ();
        _tcpClient = null;
      }
    }

    private void releaseCommonResources ()
    {
      if (_fragmentsBuffer != null) {
        _fragmentsBuffer.Dispose ();
        _fragmentsBuffer = null;
        _inContinuation = false;
      }

      if (_pongReceived != null) {
        _pongReceived.Close ();
        _pongReceived = null;
      }

      if (_receivingExited != null) {
        _receivingExited.Close ();
        _receivingExited = null;
      }
    }

    private void releaseResources ()
    {
      if (_client)
        releaseClientResources ();
      else
        releaseServerResources ();

      releaseCommonResources ();
    }

    // As server
    private void releaseServerResources ()
    {
      if (_closeContext == null)
        return;

      _closeContext ();
      _closeContext = null;
      _stream = null;
      _context = null;
    }

    private bool send (Opcode opcode, Stream stream)
    {
      lock (_forSend) {
        var src = stream;
        var compressed = false;
        var sent = false;
        try {
          if (_compression != CompressionMethod.None) {
            stream = stream.Compress (_compression);
            compressed = true;
          }

          sent = send (opcode, stream, compressed);
          if (!sent)
            error ("A send has been interrupted.", null);
        }
        catch (Exception ex) {
          _logger.Error (ex.ToString ());
          error ("An error has occurred during a send.", ex);
        }
        finally {
          if (compressed)
            stream.Dispose ();

          src.Dispose ();
        }

        return sent;
      }
    }

    private bool send (Opcode opcode, Stream stream, bool compressed)
    {
      var len = stream.Length;
      if (len == 0)
        return send (Fin.Final, opcode, EmptyBytes, false);

      var quo = len / FragmentLength;
      var rem = (int) (len % FragmentLength);

      byte[] buff = null;
      if (quo == 0) {
        buff = new byte[rem];
        return stream.Read (buff, 0, rem) == rem
               && send (Fin.Final, opcode, buff, compressed);
      }

      if (quo == 1 && rem == 0) {
        buff = new byte[FragmentLength];
        return stream.Read (buff, 0, FragmentLength) == FragmentLength
               && send (Fin.Final, opcode, buff, compressed);
      }

      /* Send fragments */

      // Begin
      buff = new byte[FragmentLength];
      var sent = stream.Read (buff, 0, FragmentLength) == FragmentLength
                 && send (Fin.More, opcode, buff, compressed);

      if (!sent)
        return false;

      var n = rem == 0 ? quo - 2 : quo - 1;
      for (long i = 0; i < n; i++) {
        sent = stream.Read (buff, 0, FragmentLength) == FragmentLength
               && send (Fin.More, Opcode.Cont, buff, false);

        if (!sent)
          return false;
      }

      // End
      if (rem == 0)
        rem = FragmentLength;
      else
        buff = new byte[rem];

      return stream.Read (buff, 0, rem) == rem
             && send (Fin.Final, Opcode.Cont, buff, false);
    }

    private bool send (Fin fin, Opcode opcode, byte[] data, bool compressed)
    {
      lock (_forState) {
        if (_readyState != WebSocketState.Open) {
          _logger.Error ("The connection is closing.");
          return false;
        }

        var frame = new WebSocketFrame (fin, opcode, data, compressed, _client);
        return sendBytes (frame.ToArray ());
      }
    }

    private void sendAsync (Opcode opcode, Stream stream, Action<bool> completed)
    {
      System.Threading.Tasks.Task.Run(() => send(opcode, stream)).ContinueWith(sent => {
          try {
            if (completed != null)
              completed(sent.Result);
          }
          catch (Exception ex) {
            _logger.Error (ex.ToString ());
            error (
              "An error has occurred during the callback for an async send.",
              ex
            );
          }
      });
    }

    private bool sendBytes (byte[] bytes)
    {
      try {
        _stream.Write (bytes, 0, bytes.Length);
      }
      catch (Exception ex) {
        _logger.Error (ex.Message);
        _logger.Debug (ex.ToString ());

        return false;
      }

      return true;
    }

    // As client
    private HttpResponse sendHandshakeRequest ()
    {
      var req = createHandshakeRequest ();
      var res = sendHttpRequest (req, 90000);
      if (res.IsUnauthorized) {
        var chal = res.Headers["WWW-Authenticate"];
        _logger.Warn (String.Format ("Received an authentication requirement for '{0}'.", chal));
        if (chal.IsNullOrEmpty ()) {
          _logger.Error ("No authentication challenge is specified.");
          return res;
        }

        _authChallenge = AuthenticationChallenge.Parse (chal);
        if (_authChallenge == null) {
          _logger.Error ("An invalid authentication challenge is specified.");
          return res;
        }

        if (_credentials != null &&
            (!_preAuth || _authChallenge.Scheme == AuthenticationSchemes.Digest)) {
          if (res.HasConnectionClose) {
            releaseClientResources ();
            setClientStream ();
          }

          var authRes = new AuthenticationResponse (_authChallenge, _credentials, _nonceCount);
          _nonceCount = authRes.NonceCount;
          req.Headers["Authorization"] = authRes.ToString ();
          res = sendHttpRequest (req, 15000);
        }
      }

      if (res.IsRedirect) {
        var url = res.Headers["Location"];
        _logger.Warn (String.Format ("Received a redirection to '{0}'.", url));
        if (_enableRedirection) {
          if (url.IsNullOrEmpty ()) {
            _logger.Error ("No url to redirect is located.");
            return res;
          }

          Uri uri;
          string msg;
          if (!url.TryCreateWebSocketUri (out uri, out msg)) {
            _logger.Error ("An invalid url to redirect is located: " + msg);
            return res;
          }

          releaseClientResources ();

          _uri = uri;
          _secure = uri.Scheme == "wss";

          setClientStream ();
          return sendHandshakeRequest ();
        }
      }

      return res;
    }

    // As client
    private HttpResponse sendHttpRequest (HttpRequest request, int millisecondsTimeout)
    {
      _logger.Debug ("A request to the server:\n" + request.ToString ());
      var res = request.GetResponse (_stream, millisecondsTimeout);
      _logger.Debug ("A response to this request:\n" + res.ToString ());

      return res;
    }

    // As server
    private bool sendHttpResponse (HttpResponse response)
    {
      _logger.Debug ("A response to this request:\n" + response.ToString ());
      return sendBytes (response.ToByteArray ());
    }

    // As client
    private void sendProxyConnectRequest ()
    {
      var req = HttpRequest.CreateConnectRequest (_uri);
      var res = sendHttpRequest (req, 90000);
      if (res.IsProxyAuthenticationRequired) {
        var chal = res.Headers["Proxy-Authenticate"];
        _logger.Warn (
          String.Format ("Received a proxy authentication requirement for '{0}'.", chal));

        if (chal.IsNullOrEmpty ())
          throw new WebSocketException ("No proxy authentication challenge is specified.");

        var authChal = AuthenticationChallenge.Parse (chal);
        if (authChal == null)
          throw new WebSocketException ("An invalid proxy authentication challenge is specified.");

        if (_proxyCredentials != null) {
          if (res.HasConnectionClose) {
            releaseClientResources ();
            _tcpClient = new TcpClient (_proxyUri.DnsSafeHost, _proxyUri.Port);
            _stream = _tcpClient.GetStream ();
          }

          var authRes = new AuthenticationResponse (authChal, _proxyCredentials, 0);
          req.Headers["Proxy-Authorization"] = authRes.ToString ();
          res = sendHttpRequest (req, 15000);
        }

        if (res.IsProxyAuthenticationRequired)
          throw new WebSocketException ("A proxy authentication is required.");
      }

      if (res.StatusCode[0] != '2')
        throw new WebSocketException (
          "The proxy has failed a connection to the requested host and port.");
    }

    // As client
    private void setClientStream ()
    {
      if (_proxyUri != null) {
        _tcpClient = new TcpClient (_proxyUri.DnsSafeHost, _proxyUri.Port);
        _stream = _tcpClient.GetStream ();
        sendProxyConnectRequest ();
      }
      else {
        _tcpClient = new TcpClient (_uri.DnsSafeHost, _uri.Port);
        _stream = _tcpClient.GetStream ();
      }

      if (_secure) {
        var conf = SslConfiguration;
        var host = conf.TargetHost;
        if (host != _uri.DnsSafeHost)
          throw new WebSocketException (
            CloseStatusCode.TlsHandshakeFailure, "An invalid host name is specified.");

        try {
          var sslStream = new SslStream (
            _stream,
            false,
            conf.ServerCertificateValidationCallback,
            conf.ClientCertificateSelectionCallback);

          sslStream.AuthenticateAsClient (
            host,
            conf.ClientCertificates,
            conf.EnabledSslProtocols,
            conf.CheckCertificateRevocation);

          _stream = sslStream;
        }
        catch (Exception ex) {
          throw new WebSocketException (CloseStatusCode.TlsHandshakeFailure, ex);
        }
      }
    }

    private void startReceiving ()
    {
      if (_messageEventQueue.Count > 0)
        _messageEventQueue.Clear ();

      _pongReceived = new ManualResetEvent (false);
      _receivingExited = new ManualResetEvent (false);

      Action receive = null;
      receive =
        () =>
          WebSocketFrame.ReadFrameAsync (
            _stream,
            false,
            frame => {
              if (!processReceivedFrame (frame) || _readyState == WebSocketState.Closed) {
                var exited = _receivingExited;
                if (exited != null)
                  exited.Set ();

                return;
              }

              // Receive next asap because the Ping or Close needs a response to it.
              receive ();

              if (_inMessage || !HasMessage || _readyState != WebSocketState.Open)
                return;

              message ();
            },
            ex => {
              _logger.Fatal (ex.ToString ());
              fatal ("An exception has occurred while receiving.", ex);
            }
          );

      receive ();
    }

    // As client
    private bool validateSecWebSocketAcceptHeader (string value)
    {
      return value != null && value == CreateResponseKey (_base64Key);
    }

    // As server
    private bool validateSecWebSocketExtensionsClientHeader (string value)
    {
      return value == null || value.Length > 0;
    }

    // As client
    private bool validateSecWebSocketExtensionsServerHeader (string value)
    {
      if (value == null)
        return true;

      if (value.Length == 0)
        return false;

      if (!_extensionsRequested)
        return false;

      var comp = _compression != CompressionMethod.None;
      foreach (var e in value.SplitHeaderValue (',')) {
        var ext = e.Trim ();
        if (comp && ext.IsCompressionExtension (_compression)) {
          if (!ext.Contains ("server_no_context_takeover")) {
            _logger.Error ("The server hasn't sent back 'server_no_context_takeover'.");
            return false;
          }

          if (!ext.Contains ("client_no_context_takeover"))
            _logger.Warn ("The server hasn't sent back 'client_no_context_takeover'.");

          var method = _compression.ToExtensionString ();
          var invalid =
            ext.SplitHeaderValue (';').Contains (
              t => {
                t = t.Trim ();
                return t != method
                       && t != "server_no_context_takeover"
                       && t != "client_no_context_takeover";
              }
            );

          if (invalid)
            return false;
        }
        else {
          return false;
        }
      }

      return true;
    }

    // As server
    private bool validateSecWebSocketKeyHeader (string value)
    {
      return value != null && value.Length > 0;
    }

    // As server
    private bool validateSecWebSocketProtocolClientHeader (string value)
    {
      return value == null || value.Length > 0;
    }

    // As client
    private bool validateSecWebSocketProtocolServerHeader (string value)
    {
      if (value == null)
        return !_protocolsRequested;

      if (value.Length == 0)
        return false;

      return _protocolsRequested && _protocols.Contains (p => p == value);
    }

    // As server
    private bool validateSecWebSocketVersionClientHeader (string value)
    {
      return value != null && value == _version;
    }

    // As client
    private bool validateSecWebSocketVersionServerHeader (string value)
    {
      return value == null || value == _version;
    }

    #endregion

    #region Internal Methods

    // As server
    internal void Close (HttpResponse response)
    {
      _readyState = WebSocketState.Closing;

      sendHttpResponse (response);
      releaseServerResources ();

      _readyState = WebSocketState.Closed;
    }

    // As server
    internal void Close (HttpStatusCode code)
    {
      Close (createHandshakeFailureResponse (code));
    }

    // As server
    internal void Close (PayloadData payloadData, byte[] frameAsBytes)
    {
      lock (_forState) {
        if (_readyState == WebSocketState.Closing) {
          _logger.Info ("The closing is already in progress.");
          return;
        }

        if (_readyState == WebSocketState.Closed) {
          _logger.Info ("The connection has already been closed.");
          return;
        }

        _readyState = WebSocketState.Closing;
      }

      _logger.Trace ("Begin closing the connection.");

      var sent = frameAsBytes != null && sendBytes (frameAsBytes);
      var received = sent && _receivingExited != null
                     ? _receivingExited.WaitOne (_waitTime)
                     : false;

      var res = sent && received;

      _logger.Debug (
        String.Format (
          "Was clean?: {0}\n  sent: {1}\n  received: {2}", res, sent, received
        )
      );

      releaseServerResources ();
      releaseCommonResources ();

      _logger.Trace ("End closing the connection.");

      _readyState = WebSocketState.Closed;

      var e = new CloseEventArgs (payloadData);
      e.WasClean = res;

      try {
        OnClose.Emit (this, e);
      }
      catch (Exception ex) {
        _logger.Error (ex.ToString ());
      }
    }

    // As client
    internal static string CreateBase64Key ()
    {
      var src = new byte[16];
      RandomNumber.GetBytes (src);

      return Convert.ToBase64String (src);
    }

    internal static string CreateResponseKey (string base64Key)
    {
      var buff = new StringBuilder (base64Key, 64);
      buff.Append (_guid);
      SHA1 sha1 = new SHA1CryptoServiceProvider ();
      var src = sha1.ComputeHash (buff.ToString ().UTF8Encode ());

      return Convert.ToBase64String (src);
    }

    // As server
    internal void InternalAccept ()
    {
      try {
        if (!acceptHandshake ())
          return;

        _readyState = WebSocketState.Open;
      }
      catch (Exception ex) {
        _logger.Fatal (ex.ToString ());
        fatal ("An exception has occurred while accepting.", ex);

        return;
      }

      open ();
    }

    // As server
    internal bool Ping (byte[] frameAsBytes, TimeSpan timeout)
    {
      if (_readyState != WebSocketState.Open)
        return false;

      var pongReceived = _pongReceived;
      if (pongReceived == null)
        return false;

      lock (_forPing) {
        try {
          pongReceived.Reset ();

          lock (_forState) {
            if (_readyState != WebSocketState.Open)
              return false;

            if (!sendBytes (frameAsBytes))
              return false;
          }

          return pongReceived.WaitOne (timeout);
        }
        catch (ObjectDisposedException) {
          return false;
        }
      }
    }

    // As server
    internal void Send (
      Opcode opcode, byte[] data, Dictionary<CompressionMethod, byte[]> cache
    )
    {
      lock (_forSend) {
        lock (_forState) {
          if (_readyState != WebSocketState.Open) {
            _logger.Error ("The connection is closing.");
            return;
          }

          byte[] found;
          if (!cache.TryGetValue (_compression, out found)) {
            found = new WebSocketFrame (
                      Fin.Final,
                      opcode,
                      data.Compress (_compression),
                      _compression != CompressionMethod.None,
                      false
                    )
                    .ToArray ();

            cache.Add (_compression, found);
          }

          sendBytes (found);
        }
      }
    }

    // As server
    internal void Send (
      Opcode opcode, Stream stream, Dictionary<CompressionMethod, Stream> cache
    )
    {
      lock (_forSend) {
        Stream found;
        if (!cache.TryGetValue (_compression, out found)) {
          found = stream.Compress (_compression);
          cache.Add (_compression, found);
        }
        else {
          found.Position = 0;
        }

        send (opcode, found, _compression != CompressionMethod.None);
      }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Accepts the WebSocket handshake request.
    /// </summary>
    /// <remarks>
    /// This method is not available in a client.
    /// </remarks>
    public void Accept ()
    {
      string msg;
      if (!checkIfAvailable (false, true, true, false, false, false, out msg)) {
        _logger.Error (msg);
        error ("An error has occurred in accepting.", null);

        return;
      }

      if (accept ())
        open ();
    }

    /// <summary>
    /// Accepts the WebSocket handshake request asynchronously.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This method does not wait for the accept to be complete.
    ///   </para>
    ///   <para>
    ///   This method is not available in a client.
    ///   </para>
    /// </remarks>
    public void AcceptAsync ()
    {
      string msg;
      if (!checkIfAvailable (false, true, true, false, false, false, out msg)) {
        _logger.Error (msg);
        error ("An error has occurred in accepting.", null);

        return;
      }

      System.Threading.Tasks.Task.Run(accept).ContinueWith(b => open());
    }

    /// <summary>
    /// Closes the connection.
    /// </summary>
    /// <remarks>
    /// This method does nothing if the current state of the connection is
    /// Closing or Closed.
    /// </remarks>
    public void Close ()
    {
      close (1005, String.Empty);
    }

    /// <summary>
    /// Closes the connection with the specified <paramref name="code"/>.
    /// </summary>
    /// <remarks>
    /// This method does nothing if the current state of the connection is
    /// Closing or Closed.
    /// </remarks>
    /// <param name="code">
    ///   <para>
    ///   A <see cref="ushort"/> that represents the status code
    ///   indicating the reason for the close.
    ///   </para>
    ///   <para>
    ///   The status codes are defined in
    ///   <see href="http://tools.ietf.org/html/rfc6455#section-7.4">
    ///   Section 7.4</see> of RFC 6455.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="code"/> is less than 1000 or greater than 4999.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="code"/> is 1011 (server error).
    ///   It cannot be used by clients.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is 1010 (mandatory extension).
    ///   It cannot be used by servers.
    ///   </para>
    /// </exception>
    public void Close (ushort code)
    {
      if (!code.IsCloseStatusCode ()) {
        var msg = "Less than 1000 or greater than 4999.";
        throw new ArgumentOutOfRangeException ("code", msg);
      }

      if (_client && code == 1011) {
        var msg = "1011 cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      if (!_client && code == 1010) {
        var msg = "1010 cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      close (code, String.Empty);
    }

    /// <summary>
    /// Closes the connection with the specified <paramref name="code"/>.
    /// </summary>
    /// <remarks>
    /// This method does nothing if the current state of the connection is
    /// Closing or Closed.
    /// </remarks>
    /// <param name="code">
    ///   <para>
    ///   One of the <see cref="CloseStatusCode"/> enum values.
    ///   </para>
    ///   <para>
    ///   It represents the status code indicating the reason for the close.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="code"/> is
    ///   <see cref="CloseStatusCode.ServerError"/>.
    ///   It cannot be used by clients.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is
    ///   <see cref="CloseStatusCode.MandatoryExtension"/>.
    ///   It cannot be used by servers.
    ///   </para>
    /// </exception>
    public void Close (CloseStatusCode code)
    {
      if (_client && code == CloseStatusCode.ServerError) {
        var msg = "ServerError cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      if (!_client && code == CloseStatusCode.MandatoryExtension) {
        var msg = "MandatoryExtension cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      close ((ushort) code, String.Empty);
    }

    /// <summary>
    /// Closes the connection with the specified <paramref name="code"/> and
    /// <paramref name="reason"/>.
    /// </summary>
    /// <remarks>
    /// This method does nothing if the current state of the connection is
    /// Closing or Closed.
    /// </remarks>
    /// <param name="code">
    ///   <para>
    ///   A <see cref="ushort"/> that represents the status code
    ///   indicating the reason for the close.
    ///   </para>
    ///   <para>
    ///   The status codes are defined in
    ///   <see href="http://tools.ietf.org/html/rfc6455#section-7.4">
    ///   Section 7.4</see> of RFC 6455.
    ///   </para>
    /// </param>
    /// <param name="reason">
    ///   <para>
    ///   A <see cref="string"/> that represents the reason for the close.
    ///   </para>
    ///   <para>
    ///   The size must be 123 bytes or less in UTF-8.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <para>
    ///   <paramref name="code"/> is less than 1000 or greater than 4999.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The size of <paramref name="reason"/> is greater than 123 bytes.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="code"/> is 1011 (server error).
    ///   It cannot be used by clients.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is 1010 (mandatory extension).
    ///   It cannot be used by servers.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is 1005 (no status) and
    ///   there is <paramref name="reason"/>.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="reason"/> could not be UTF-8-encoded.
    ///   </para>
    /// </exception>
    public void Close (ushort code, string reason)
    {
      if (!code.IsCloseStatusCode ()) {
        var msg = "Less than 1000 or greater than 4999.";
        throw new ArgumentOutOfRangeException ("code", msg);
      }

      if (_client && code == 1011) {
        var msg = "1011 cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      if (!_client && code == 1010) {
        var msg = "1010 cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      if (reason.IsNullOrEmpty ()) {
        close (code, String.Empty);
        return;
      }

      if (code == 1005) {
        var msg = "1005 cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      byte[] bytes;
      if (!reason.TryGetUTF8EncodedBytes (out bytes)) {
        var msg = "It could not be UTF-8-encoded.";
        throw new ArgumentException (msg, "reason");
      }

      if (bytes.Length > 123) {
        var msg = "Its size is greater than 123 bytes.";
        throw new ArgumentOutOfRangeException ("reason", msg);
      }

      close (code, reason);
    }

    /// <summary>
    /// Closes the connection with the specified <paramref name="code"/> and
    /// <paramref name="reason"/>.
    /// </summary>
    /// <remarks>
    /// This method does nothing if the current state of the connection is
    /// Closing or Closed.
    /// </remarks>
    /// <param name="code">
    ///   <para>
    ///   One of the <see cref="CloseStatusCode"/> enum values.
    ///   </para>
    ///   <para>
    ///   It represents the status code indicating the reason for the close.
    ///   </para>
    /// </param>
    /// <param name="reason">
    ///   <para>
    ///   A <see cref="string"/> that represents the reason for the close.
    ///   </para>
    ///   <para>
    ///   The size must be 123 bytes or less in UTF-8.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="code"/> is
    ///   <see cref="CloseStatusCode.ServerError"/>.
    ///   It cannot be used by clients.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is
    ///   <see cref="CloseStatusCode.MandatoryExtension"/>.
    ///   It cannot be used by servers.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is
    ///   <see cref="CloseStatusCode.NoStatus"/> and
    ///   there is <paramref name="reason"/>.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="reason"/> could not be UTF-8-encoded.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The size of <paramref name="reason"/> is greater than 123 bytes.
    /// </exception>
    public void Close (CloseStatusCode code, string reason)
    {
      if (_client && code == CloseStatusCode.ServerError) {
        var msg = "ServerError cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      if (!_client && code == CloseStatusCode.MandatoryExtension) {
        var msg = "MandatoryExtension cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      if (reason.IsNullOrEmpty ()) {
        close ((ushort) code, String.Empty);
        return;
      }

      if (code == CloseStatusCode.NoStatus) {
        var msg = "NoStatus cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      byte[] bytes;
      if (!reason.TryGetUTF8EncodedBytes (out bytes)) {
        var msg = "It could not be UTF-8-encoded.";
        throw new ArgumentException (msg, "reason");
      }

      if (bytes.Length > 123) {
        var msg = "Its size is greater than 123 bytes.";
        throw new ArgumentOutOfRangeException ("reason", msg);
      }

      close ((ushort) code, reason);
    }

    /// <summary>
    /// Closes the connection asynchronously.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This method does not wait for the close to be complete.
    ///   </para>
    ///   <para>
    ///   And this method does nothing if the current state of
    ///   the connection is Closing or Closed.
    ///   </para>
    /// </remarks>
    public void CloseAsync ()
    {
      closeAsync (1005, String.Empty);
    }

    /// <summary>
    /// Closes the connection asynchronously with the specified
    /// <paramref name="code"/>.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This method does not wait for the close to be complete.
    ///   </para>
    ///   <para>
    ///   And this method does nothing if the current state of
    ///   the connection is Closing or Closed.
    ///   </para>
    /// </remarks>
    /// <param name="code">
    ///   <para>
    ///   A <see cref="ushort"/> that represents the status code
    ///   indicating the reason for the close.
    ///   </para>
    ///   <para>
    ///   The status codes are defined in
    ///   <see href="http://tools.ietf.org/html/rfc6455#section-7.4">
    ///   Section 7.4</see> of RFC 6455.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="code"/> is less than 1000 or greater than 4999.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="code"/> is 1011 (server error).
    ///   It cannot be used by clients.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is 1010 (mandatory extension).
    ///   It cannot be used by servers.
    ///   </para>
    /// </exception>
    public void CloseAsync (ushort code)
    {
      if (!code.IsCloseStatusCode ()) {
        var msg = "Less than 1000 or greater than 4999.";
        throw new ArgumentOutOfRangeException ("code", msg);
      }

      if (_client && code == 1011) {
        var msg = "1011 cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      if (!_client && code == 1010) {
        var msg = "1010 cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      closeAsync (code, String.Empty);
    }

    /// <summary>
    /// Closes the connection asynchronously with the specified
    /// <paramref name="code"/>.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This method does not wait for the close to be complete.
    ///   </para>
    ///   <para>
    ///   And this method does nothing if the current state of
    ///   the connection is Closing or Closed.
    ///   </para>
    /// </remarks>
    /// <param name="code">
    ///   <para>
    ///   One of the <see cref="CloseStatusCode"/> enum values.
    ///   </para>
    ///   <para>
    ///   It represents the status code indicating the reason for the close.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="code"/> is
    ///   <see cref="CloseStatusCode.ServerError"/>.
    ///   It cannot be used by clients.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is
    ///   <see cref="CloseStatusCode.MandatoryExtension"/>.
    ///   It cannot be used by servers.
    ///   </para>
    /// </exception>
    public void CloseAsync (CloseStatusCode code)
    {
      if (_client && code == CloseStatusCode.ServerError) {
        var msg = "ServerError cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      if (!_client && code == CloseStatusCode.MandatoryExtension) {
        var msg = "MandatoryExtension cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      closeAsync ((ushort) code, String.Empty);
    }

    /// <summary>
    /// Closes the connection asynchronously with the specified
    /// <paramref name="code"/> and <paramref name="reason"/>.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This method does not wait for the close to be complete.
    ///   </para>
    ///   <para>
    ///   And this method does nothing if the current state of
    ///   the connection is Closing or Closed.
    ///   </para>
    /// </remarks>
    /// <param name="code">
    ///   <para>
    ///   A <see cref="ushort"/> that represents the status code
    ///   indicating the reason for the close.
    ///   </para>
    ///   <para>
    ///   The status codes are defined in
    ///   <see href="http://tools.ietf.org/html/rfc6455#section-7.4">
    ///   Section 7.4</see> of RFC 6455.
    ///   </para>
    /// </param>
    /// <param name="reason">
    ///   <para>
    ///   A <see cref="string"/> that represents the reason for the close.
    ///   </para>
    ///   <para>
    ///   The size must be 123 bytes or less in UTF-8.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <para>
    ///   <paramref name="code"/> is less than 1000 or greater than 4999.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The size of <paramref name="reason"/> is greater than 123 bytes.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="code"/> is 1011 (server error).
    ///   It cannot be used by clients.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is 1010 (mandatory extension).
    ///   It cannot be used by servers.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is 1005 (no status) and
    ///   there is <paramref name="reason"/>.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="reason"/> could not be UTF-8-encoded.
    ///   </para>
    /// </exception>
    public void CloseAsync (ushort code, string reason)
    {
      if (!code.IsCloseStatusCode ()) {
        var msg = "Less than 1000 or greater than 4999.";
        throw new ArgumentOutOfRangeException ("code", msg);
      }

      if (_client && code == 1011) {
        var msg = "1011 cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      if (!_client && code == 1010) {
        var msg = "1010 cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      if (reason.IsNullOrEmpty ()) {
        closeAsync (code, String.Empty);
        return;
      }

      if (code == 1005) {
        var msg = "1005 cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      byte[] bytes;
      if (!reason.TryGetUTF8EncodedBytes (out bytes)) {
        var msg = "It could not be UTF-8-encoded.";
        throw new ArgumentException (msg, "reason");
      }

      if (bytes.Length > 123) {
        var msg = "Its size is greater than 123 bytes.";
        throw new ArgumentOutOfRangeException ("reason", msg);
      }

      closeAsync (code, reason);
    }

    /// <summary>
    /// Closes the connection asynchronously with the specified
    /// <paramref name="code"/> and <paramref name="reason"/>.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This method does not wait for the close to be complete.
    ///   </para>
    ///   <para>
    ///   And this method does nothing if the current state of
    ///   the connection is Closing or Closed.
    ///   </para>
    /// </remarks>
    /// <param name="code">
    ///   <para>
    ///   One of the <see cref="CloseStatusCode"/> enum values.
    ///   </para>
    ///   <para>
    ///   It represents the status code indicating the reason for the close.
    ///   </para>
    /// </param>
    /// <param name="reason">
    ///   <para>
    ///   A <see cref="string"/> that represents the reason for the close.
    ///   </para>
    ///   <para>
    ///   The size must be 123 bytes or less in UTF-8.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="code"/> is
    ///   <see cref="CloseStatusCode.ServerError"/>.
    ///   It cannot be used by clients.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is
    ///   <see cref="CloseStatusCode.MandatoryExtension"/>.
    ///   It cannot be used by servers.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is
    ///   <see cref="CloseStatusCode.NoStatus"/> and
    ///   there is <paramref name="reason"/>.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="reason"/> could not be UTF-8-encoded.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The size of <paramref name="reason"/> is greater than 123 bytes.
    /// </exception>
    public void CloseAsync (CloseStatusCode code, string reason)
    {
      if (_client && code == CloseStatusCode.ServerError) {
        var msg = "ServerError cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      if (!_client && code == CloseStatusCode.MandatoryExtension) {
        var msg = "MandatoryExtension cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      if (reason.IsNullOrEmpty ()) {
        closeAsync ((ushort) code, String.Empty);
        return;
      }

      if (code == CloseStatusCode.NoStatus) {
        var msg = "NoStatus cannot be used.";
        throw new ArgumentException (msg, "code");
      }

      byte[] bytes;
      if (!reason.TryGetUTF8EncodedBytes (out bytes)) {
        var msg = "It could not be UTF-8-encoded.";
        throw new ArgumentException (msg, "reason");
      }

      if (bytes.Length > 123) {
        var msg = "Its size is greater than 123 bytes.";
        throw new ArgumentOutOfRangeException ("reason", msg);
      }

      closeAsync ((ushort) code, reason);
    }

    /// <summary>
    /// Establishes a WebSocket connection.
    /// </summary>
    /// <remarks>
    /// This method is not available in a server.
    /// </remarks>
    public void Connect ()
    {
      string msg;
      if (!checkIfAvailable (true, false, true, false, false, true, out msg)) {
        _logger.Error (msg);
        error ("An error has occurred in connecting.", null);

        return;
      }

      if (connect ())
        open ();
    }

    /// <summary>
    /// Establishes a WebSocket connection asynchronously.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This method does not wait for the connect to be complete.
    ///   </para>
    ///   <para>
    ///   This method is not available in a server.
    ///   </para>
    /// </remarks>
    public void ConnectAsync ()
    {
      string msg;
      if (!checkIfAvailable (true, false, true, false, false, true, out msg)) {
        _logger.Error (msg);
        error ("An error has occurred in connecting.", null);

        return;
      }

      System.Threading.Tasks.Task.Run(connect).ContinueWith(b => open());
    }

    /// <summary>
    /// Sends a ping using the WebSocket connection.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the send has done with no error and a pong has been
    /// received within a time; otherwise, <c>false</c>.
    /// </returns>
    public bool Ping ()
    {
      return ping (EmptyBytes);
    }

    /// <summary>
    /// Sends a ping with <paramref name="message"/> using the WebSocket
    /// connection.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the send has done with no error and a pong has been
    /// received within a time; otherwise, <c>false</c>.
    /// </returns>
    /// <param name="message">
    ///   <para>
    ///   A <see cref="string"/> that represents the message to send.
    ///   </para>
    ///   <para>
    ///   The size must be 125 bytes or less in UTF-8.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="message"/> could not be UTF-8-encoded.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The size of <paramref name="message"/> is greater than 125 bytes.
    /// </exception>
    public bool Ping (string message)
    {
      if (message.IsNullOrEmpty ())
        return ping (EmptyBytes);

      byte[] bytes;
      if (!message.TryGetUTF8EncodedBytes (out bytes)) {
        var msg = "It could not be UTF-8-encoded.";
        throw new ArgumentException (msg, "message");
      }

      if (bytes.Length > 125) {
        var msg = "Its size is greater than 125 bytes.";
        throw new ArgumentOutOfRangeException ("message", msg);
      }

      return ping (bytes);
    }

    /// <summary>
    /// Sends <paramref name="data"/> using the WebSocket connection.
    /// </summary>
    /// <param name="data">
    /// An array of <see cref="byte"/> that represents the binary data to send.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The current state of the connection is not Open.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="data"/> is <see langword="null"/>.
    /// </exception>
    public void Send (byte[] data)
    {
      if (_readyState != WebSocketState.Open) {
        var msg = "The current state of the connection is not Open.";
        throw new InvalidOperationException (msg);
      }

      if (data == null)
        throw new ArgumentNullException ("data");

      send (Opcode.Binary, new MemoryStream (data));
    }

    /// <summary>
    /// Sends the specified file using the WebSocket connection.
    /// </summary>
    /// <remarks>
    /// The file is sent as the binary data.
    /// </remarks>
    /// <param name="fileInfo">
    /// A <see cref="FileInfo"/> that specifies the file to send.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The current state of the connection is not Open.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="fileInfo"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   The file does not exist.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The file could not be opened.
    ///   </para>
    /// </exception>
    public void Send (FileInfo fileInfo)
    {
      if (_readyState != WebSocketState.Open) {
        var msg = "The current state of the connection is not Open.";
        throw new InvalidOperationException (msg);
      }

      if (fileInfo == null)
        throw new ArgumentNullException ("fileInfo");

      if (!fileInfo.Exists) {
        var msg = "The file does not exist.";
        throw new ArgumentException (msg, "fileInfo");
      }

      FileStream stream;
      if (!fileInfo.TryOpenRead (out stream)) {
        var msg = "The file could not be opened.";
        throw new ArgumentException (msg, "fileInfo");
      }

      send (Opcode.Binary, stream);
    }

    /// <summary>
    /// Sends <paramref name="data"/> using the WebSocket connection.
    /// </summary>
    /// <param name="data">
    /// A <see cref="string"/> that represents the text data to send.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The current state of the connection is not Open.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="data"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="data"/> could not be UTF-8-encoded.
    /// </exception>
    public void Send (string data)
    {
      if (_readyState != WebSocketState.Open) {
        var msg = "The current state of the connection is not Open.";
        throw new InvalidOperationException (msg);
      }

      if (data == null)
        throw new ArgumentNullException ("data");

      byte[] bytes;
      if (!data.TryGetUTF8EncodedBytes (out bytes)) {
        var msg = "It could not be UTF-8-encoded.";
        throw new ArgumentException (msg, "data");
      }

      send (Opcode.Text, new MemoryStream (bytes));
    }

    /// <summary>
    /// Sends the data from <paramref name="stream"/> using the WebSocket
    /// connection.
    /// </summary>
    /// <remarks>
    /// The data is sent as the binary data.
    /// </remarks>
    /// <param name="stream">
    /// A <see cref="Stream"/> instance from which to read the data to send.
    /// </param>
    /// <param name="length">
    /// An <see cref="int"/> that specifies the number of bytes to send.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The current state of the connection is not Open.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="stream"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="stream"/> cannot be read.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="length"/> is less than 1.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   No data could be read from <paramref name="stream"/>.
    ///   </para>
    /// </exception>
    public void Send (Stream stream, int length)
    {
      if (_readyState != WebSocketState.Open) {
        var msg = "The current state of the connection is not Open.";
        throw new InvalidOperationException (msg);
      }

      if (stream == null)
        throw new ArgumentNullException ("stream");

      if (!stream.CanRead) {
        var msg = "It cannot be read.";
        throw new ArgumentException (msg, "stream");
      }

      if (length < 1) {
        var msg = "Less than 1.";
        throw new ArgumentException (msg, "length");
      }

      var bytes = stream.ReadBytes (length);

      var len = bytes.Length;
      if (len == 0) {
        var msg = "No data could be read from it.";
        throw new ArgumentException (msg, "stream");
      }

      if (len < length) {
        _logger.Warn (
          String.Format (
            "Only {0} byte(s) of data could be read from the stream.",
            len
          )
        );
      }

      send (Opcode.Binary, new MemoryStream (bytes));
    }

    /// <summary>
    /// Sends <paramref name="data"/> asynchronously using the WebSocket
    /// connection.
    /// </summary>
    /// <remarks>
    /// This method does not wait for the send to be complete.
    /// </remarks>
    /// <param name="data">
    /// An array of <see cref="byte"/> that represents the binary data to send.
    /// </param>
    /// <param name="completed">
    ///   <para>
    ///   An <c>Action&lt;bool&gt;</c> delegate or <see langword="null"/>
    ///   if not needed.
    ///   </para>
    ///   <para>
    ///   The delegate invokes the method called when the send is complete.
    ///   </para>
    ///   <para>
    ///   <c>true</c> is passed to the method if the send has done with
    ///   no error; otherwise, <c>false</c>.
    ///   </para>
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The current state of the connection is not Open.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="data"/> is <see langword="null"/>.
    /// </exception>
    public void SendAsync (byte[] data, Action<bool> completed)
    {
      if (_readyState != WebSocketState.Open) {
        var msg = "The current state of the connection is not Open.";
        throw new InvalidOperationException (msg);
      }

      if (data == null)
        throw new ArgumentNullException ("data");

      sendAsync (Opcode.Binary, new MemoryStream (data), completed);
    }

    /// <summary>
    /// Sends the specified file asynchronously using the WebSocket connection.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   The file is sent as the binary data.
    ///   </para>
    ///   <para>
    ///   This method does not wait for the send to be complete.
    ///   </para>
    /// </remarks>
    /// <param name="fileInfo">
    /// A <see cref="FileInfo"/> that specifies the file to send.
    /// </param>
    /// <param name="completed">
    ///   <para>
    ///   An <c>Action&lt;bool&gt;</c> delegate or <see langword="null"/>
    ///   if not needed.
    ///   </para>
    ///   <para>
    ///   The delegate invokes the method called when the send is complete.
    ///   </para>
    ///   <para>
    ///   <c>true</c> is passed to the method if the send has done with
    ///   no error; otherwise, <c>false</c>.
    ///   </para>
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The current state of the connection is not Open.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="fileInfo"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   The file does not exist.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The file could not be opened.
    ///   </para>
    /// </exception>
    public void SendAsync (FileInfo fileInfo, Action<bool> completed)
    {
      if (_readyState != WebSocketState.Open) {
        var msg = "The current state of the connection is not Open.";
        throw new InvalidOperationException (msg);
      }

      if (fileInfo == null)
        throw new ArgumentNullException ("fileInfo");

      if (!fileInfo.Exists) {
        var msg = "The file does not exist.";
        throw new ArgumentException (msg, "fileInfo");
      }

      FileStream stream;
      if (!fileInfo.TryOpenRead (out stream)) {
        var msg = "The file could not be opened.";
        throw new ArgumentException (msg, "fileInfo");
      }

      sendAsync (Opcode.Binary, stream, completed);
    }

    /// <summary>
    /// Sends <paramref name="data"/> asynchronously using the WebSocket
    /// connection.
    /// </summary>
    /// <remarks>
    /// This method does not wait for the send to be complete.
    /// </remarks>
    /// <param name="data">
    /// A <see cref="string"/> that represents the text data to send.
    /// </param>
    /// <param name="completed">
    ///   <para>
    ///   An <c>Action&lt;bool&gt;</c> delegate or <see langword="null"/>
    ///   if not needed.
    ///   </para>
    ///   <para>
    ///   The delegate invokes the method called when the send is complete.
    ///   </para>
    ///   <para>
    ///   <c>true</c> is passed to the method if the send has done with
    ///   no error; otherwise, <c>false</c>.
    ///   </para>
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The current state of the connection is not Open.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="data"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="data"/> could not be UTF-8-encoded.
    /// </exception>
    public void SendAsync (string data, Action<bool> completed)
    {
      if (_readyState != WebSocketState.Open) {
        var msg = "The current state of the connection is not Open.";
        throw new InvalidOperationException (msg);
      }

      if (data == null)
        throw new ArgumentNullException ("data");

      byte[] bytes;
      if (!data.TryGetUTF8EncodedBytes (out bytes)) {
        var msg = "It could not be UTF-8-encoded.";
        throw new ArgumentException (msg, "data");
      }

      sendAsync (Opcode.Text, new MemoryStream (bytes), completed);
    }

    /// <summary>
    /// Sends the data from <paramref name="stream"/> asynchronously using
    /// the WebSocket connection.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   The data is sent as the binary data.
    ///   </para>
    ///   <para>
    ///   This method does not wait for the send to be complete.
    ///   </para>
    /// </remarks>
    /// <param name="stream">
    /// A <see cref="Stream"/> instance from which to read the data to send.
    /// </param>
    /// <param name="length">
    /// An <see cref="int"/> that specifies the number of bytes to send.
    /// </param>
    /// <param name="completed">
    ///   <para>
    ///   An <c>Action&lt;bool&gt;</c> delegate or <see langword="null"/>
    ///   if not needed.
    ///   </para>
    ///   <para>
    ///   The delegate invokes the method called when the send is complete.
    ///   </para>
    ///   <para>
    ///   <c>true</c> is passed to the method if the send has done with
    ///   no error; otherwise, <c>false</c>.
    ///   </para>
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The current state of the connection is not Open.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="stream"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="stream"/> cannot be read.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="length"/> is less than 1.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   No data could be read from <paramref name="stream"/>.
    ///   </para>
    /// </exception>
    public void SendAsync (Stream stream, int length, Action<bool> completed)
    {
      if (_readyState != WebSocketState.Open) {
        var msg = "The current state of the connection is not Open.";
        throw new InvalidOperationException (msg);
      }

      if (stream == null)
        throw new ArgumentNullException ("stream");

      if (!stream.CanRead) {
        var msg = "It cannot be read.";
        throw new ArgumentException (msg, "stream");
      }

      if (length < 1) {
        var msg = "Less than 1.";
        throw new ArgumentException (msg, "length");
      }

      var bytes = stream.ReadBytes (length);

      var len = bytes.Length;
      if (len == 0) {
        var msg = "No data could be read from it.";
        throw new ArgumentException (msg, "stream");
      }

      if (len < length) {
        _logger.Warn (
          String.Format (
            "Only {0} byte(s) of data could be read from the stream.",
            len
          )
        );
      }

      sendAsync (Opcode.Binary, new MemoryStream (bytes), completed);
    }

    /// <summary>
    /// Sets an HTTP <paramref name="cookie"/> to send with
    /// the WebSocket handshake request to the server.
    /// </summary>
    /// <remarks>
    /// This method is not available in a server.
    /// </remarks>
    /// <param name="cookie">
    /// A <see cref="Cookie"/> that represents a cookie to send.
    /// </param>
    public void SetCookie (Cookie cookie)
    {
      string msg;
      if (!checkIfAvailable (true, false, true, false, false, true, out msg)) {
        _logger.Error (msg);
        error ("An error has occurred in setting a cookie.", null);

        return;
      }

      if (cookie == null) {
        _logger.Error ("'cookie' is null.");
        error ("An error has occurred in setting a cookie.", null);

        return;
      }

      lock (_forState) {
        if (!checkIfAvailable (true, false, false, true, out msg)) {
          _logger.Error (msg);
          error ("An error has occurred in setting a cookie.", null);

          return;
        }

        lock (_cookies.SyncRoot)
          _cookies.SetOrRemove (cookie);
      }
    }

    /// <summary>
    /// Sets a pair of <paramref name="username"/> and <paramref name="password"/> for
    /// the HTTP authentication (Basic/Digest).
    /// </summary>
    /// <remarks>
    /// This method is not available in a server.
    /// </remarks>
    /// <param name="username">
    ///   <para>
    ///   A <see cref="string"/> that represents the user name used to authenticate.
    ///   </para>
    ///   <para>
    ///   If <paramref name="username"/> is <see langword="null"/> or empty,
    ///   the credentials will be initialized and not be sent.
    ///   </para>
    /// </param>
    /// <param name="password">
    /// A <see cref="string"/> that represents the password for
    /// <paramref name="username"/> used to authenticate.
    /// </param>
    /// <param name="preAuth">
    /// <c>true</c> if the <see cref="WebSocket"/> sends the credentials for
    /// the Basic authentication with the first handshake request to the server;
    /// otherwise, <c>false</c>.
    /// </param>
    public void SetCredentials (string username, string password, bool preAuth)
    {
      string msg;
      if (!checkIfAvailable (true, false, true, false, false, true, out msg)) {
        _logger.Error (msg);
        error ("An error has occurred in setting the credentials.", null);

        return;
      }

      if (!checkParametersForSetCredentials (username, password, out msg)) {
        _logger.Error (msg);
        error ("An error has occurred in setting the credentials.", null);

        return;
      }

      lock (_forState) {
        if (!checkIfAvailable (true, false, false, true, out msg)) {
          _logger.Error (msg);
          error ("An error has occurred in setting the credentials.", null);

          return;
        }

        if (username.IsNullOrEmpty ()) {
          _logger.Warn ("The credentials are initialized.");
          _credentials = null;
          _preAuth = false;

          return;
        }

        _credentials = new NetworkCredential (username, password, _uri.PathAndQuery);
        _preAuth = preAuth;
      }
    }

    /// <summary>
    /// Sets the HTTP proxy server URL to connect through, and if necessary,
    /// a pair of <paramref name="username"/> and <paramref name="password"/> for
    /// the proxy server authentication (Basic/Digest).
    /// </summary>
    /// <remarks>
    /// This method is not available in a server.
    /// </remarks>
    /// <param name="url">
    ///   <para>
    ///   A <see cref="string"/> that represents the HTTP proxy server URL to
    ///   connect through. The syntax must be http://&lt;host&gt;[:&lt;port&gt;].
    ///   </para>
    ///   <para>
    ///   If <paramref name="url"/> is <see langword="null"/> or empty,
    ///   the url and credentials for the proxy will be initialized,
    ///   and the <see cref="WebSocket"/> will not use the proxy to
    ///   connect through.
    ///   </para>
    /// </param>
    /// <param name="username">
    ///   <para>
    ///   A <see cref="string"/> that represents the user name used to authenticate.
    ///   </para>
    ///   <para>
    ///   If <paramref name="username"/> is <see langword="null"/> or empty,
    ///   the credentials for the proxy will be initialized and not be sent.
    ///   </para>
    /// </param>
    /// <param name="password">
    /// A <see cref="string"/> that represents the password for
    /// <paramref name="username"/> used to authenticate.
    /// </param>
    public void SetProxy (string url, string username, string password)
    {
      string msg;
      if (!checkIfAvailable (true, false, true, false, false, true, out msg)) {
        _logger.Error (msg);
        error ("An error has occurred in setting the proxy.", null);

        return;
      }

      if (!checkParametersForSetProxy (url, username, password, out msg)) {
        _logger.Error (msg);
        error ("An error has occurred in setting the proxy.", null);

        return;
      }

      lock (_forState) {
        if (!checkIfAvailable (true, false, false, true, out msg)) {
          _logger.Error (msg);
          error ("An error has occurred in setting the proxy.", null);

          return;
        }

        if (url.IsNullOrEmpty ()) {
          _logger.Warn ("The url and credentials for the proxy are initialized.");
          _proxyUri = null;
          _proxyCredentials = null;

          return;
        }

        _proxyUri = new Uri (url);

        if (username.IsNullOrEmpty ()) {
          _logger.Warn ("The credentials for the proxy are initialized.");
          _proxyCredentials = null;

          return;
        }

        _proxyCredentials =
          new NetworkCredential (
            username, password, String.Format ("{0}:{1}", _uri.DnsSafeHost, _uri.Port)
          );
      }
    }

    #endregion

    #region Explicit Interface Implementations

    /// <summary>
    /// Closes the connection and releases all associated resources.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This method closes the connection with close status 1001 (going away).
    ///   </para>
    ///   <para>
    ///   And this method does nothing if the current state of the connection is
    ///   Closing or Closed.
    ///   </para>
    /// </remarks>
    void IDisposable.Dispose ()
    {
      close (1001, String.Empty);
    }

    #endregion
  }
}
