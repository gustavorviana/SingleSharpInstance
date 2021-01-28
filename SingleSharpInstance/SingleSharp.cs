using SingleSharpInstance.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;

namespace SingleSharpInstance
{
    public class SingleSharp
    {
        #region Static Fields

        private static readonly Dictionary<Guid, SingleSharp> Contexts = new Dictionary<Guid, SingleSharp>();
        private static readonly BinaryFormatter formatter = new BinaryFormatter();

        #endregion

        #region Private fields

        private readonly CancellationTokenSource tSource = new CancellationTokenSource();
        private readonly NamedPipeServerStream _server;
        private readonly SingleName _name;
        private int _receivedPackets = 0;
        private const int Timeout = 5000;
        private readonly Mutex _mutex;

        #endregion

        #region Public Event/Fields

        public SynchronizationContext SyncContext { get; set; } = SynchronizationContext.Current;
        public event EventHandler<ActivationEventArgs> OnReceiveActivation;
        public event ThreadExceptionEventHandler OnException;

        /// <summary>
        /// Retrieve the connection id.
        /// </summary>
        public Guid Id => this._name.Id;

        /// <summary>
        /// Signals if this is the primary instance.
        /// </summary>
        public bool IsMainInstance { get; }

        #endregion

        #region Constructor/From

        /// <summary>
        /// Creates the object responsible for receiving or sending connection data.
        /// </summary>
        /// <param name="id">Connection id.</param>
        /// <param name="userName">User pipe.</param>
        /// <param name="createServer">Signals whether the object can have a server created.</param>
        private SingleSharp(Guid id, string userName, PipeSecurity security)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException();

            this._name = new SingleName(id, userName);
            this._mutex = new Mutex(true, this._name.GetMutexName());
            this.IsMainInstance = this.Wait(0);

            if (!this.IsMainInstance)
                return;

            try
            {
                //Attempts to create a connection as a serverx
                if (security != null)
                    this._server = NamedPipeServerStreamConstructors.New(this._name.GetPipeName(), PipeDirection.In, pipeSecurity: security);
                else
                    this._server = new NamedPipeServerStream(this._name.GetPipeName(), PipeDirection.In);
            }
            catch (IOException)
            {
                //If it fails, it turns you into a customer
                this.IsMainInstance = false;
                return;
            }

            //Starts the server
            new Task(StartServer).Start();
        }

        /// <summary>
        /// Create an instance of SingleSharp or retrieve an existing one with PipeSecurity.
        /// </summary>
        /// <param name="id">Connection id.</param>
        /// <param name="userName">UserConnection (allow null).</param>
        /// <param name="security">Connection security.</param>
        /// <returns></returns>
        public static SingleSharp From(Guid id, string userName = null, PipeSecurity security = null)
        {
            if (Contexts.ContainsKey(id))
                return Contexts[id];

            if (userName == null)
                userName = Environment.UserName;

            var context = new SingleSharp(id, userName, security);
            Contexts.Add(id, context);
            return context;
        }

        /// <summary>
        /// Create an instance of SingleSharp or retrieve an existing one with PipeSecurity.
        /// </summary>
        /// <typeparam name="T">Class that should be used as a reference in the connection.</typeparam>
        /// <param name="userName">UserConnection (allow null).</param>
        /// <param name="security">Connection security.</param>
        /// <returns></returns>
        public static SingleSharp From<T>(string userName = null, PipeSecurity security = null)
        {
            return From(typeof(T).GUID, userName, security);
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Send activation arguments to the main connection.
        /// </summary>
        /// <param name="activation">Activation arguments.</param>
        /// <returns>Returns true if the value is sent and false if it is not sent.</returns>
        public bool SendActivation(params string[] args)
        {
            if (this.IsMainInstance)
            {
                this.EmitActivation(args);
                return true;
            }

            try
            {
                using var source = new CancellationTokenSource(Timeout);
                using var client = new NamedPipeClientStream(".", this._name.GetPipeName(), PipeDirection.Out);

                client.ConnectAsync(source.Token).Wait(source.Token);
                if (!client.IsConnected)
                    return false;

                using var ms = new MemoryStream();
                formatter.Serialize(ms, args);

                byte[] buffer = ms.ToArray();
                var len = buffer.Length;
                if (len > ushort.MaxValue)
                    len = ushort.MaxValue;

                byte[] lenBuffer = new byte[] { (byte)(len / 256), (byte)(len & 255) };

                //Escreve os dados na stream
                client.WriteAsync(lenBuffer, 0, lenBuffer.Length, source.Token).Wait(source.Token);
                client.WriteAsync(buffer, 0, buffer.Length, source.Token).Wait(source.Token);
                client.FlushAsync(source.Token).Wait(source.Token);
                return true;
            }
            catch (AggregateException ex)
            {
                throw ex.InnerException;
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException();
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException();
            }
        }

        /// <summary>
        /// Shutdown the server (if any), cancel all tokens and release the mutex.
        /// </summary>
        public void Shutdown()
        {
            if (this._server != null)
            {
                if (this._server.IsConnected)
                    this._server.Disconnect();

                this._server.Close();
            }

            this.tSource.Cancel();
            this._mutex.ReleaseMutex();
            this._mutex.Close();
        }

        /// <summary>
        /// Releases all resources.
        /// </summary>
        public void Dispose()
        {
            this._receivedPackets = -1;

            if (this._server != null)
                this._server.Dispose();

            this.tSource.Dispose();
            this._mutex.Dispose();
        }

        #endregion

        #region Private methods

        /// <summary>
        /// Performs the necessary processes before calling the this.OnReceiveActivation method.
        /// </summary>
        /// <param name="activation">Activation value.</param>
        private void EmitActivation(string[] args)
        {
            if (this._receivedPackets < 0)
                throw new ObjectDisposedException(this.GetType().Name);

            this._receivedPackets++;

            var _args = new ActivationEventArgs(args, this._receivedPackets == 1);
            if (SyncContext != null)
            {
                SyncContext.Post(_ => this.OnReceiveActivation?.Invoke(this, _args), null);
                return;
            }

            this.OnReceiveActivation?.Invoke(this, _args);
        }

        private async void StartServer()
        {
            CancellationTokenSource source = null;
            void CancelChildren()
            {
                if (source == null || source.IsCancellationRequested)
                    return;

                source.Cancel();
            }

            this.tSource.Token.Register(CancelChildren);
            while (!this.tSource.IsCancellationRequested)
            {
                try
                {
                    //Aguarda um novo cliente
                    await this._server.WaitForConnectionAsync(this.tSource.Token);
                    using (source = new CancellationTokenSource(Timeout))
                    {
                        byte[] lenBuffer = await ReadPacketAsync(2, source.Token);
                        int len = (lenBuffer[0] * 256) + lenBuffer[1];


                        using var ms = new MemoryStream(await ReadPacketAsync(len, source.Token));
                        var args = (string[])formatter.Deserialize(ms);

                        //Desconecta o cliente
                        this._server.Disconnect();

                        //Emite o evento com a ativação
                        this.EmitActivation(args);
                    }
                }
                catch (System.Runtime.Serialization.SerializationException)
                {
                    if (this._server.IsConnected)
                        this._server.Disconnect();

                    continue;
                }
                catch (TaskCanceledException)
                {
                    if (this._server.IsConnected)
                        this._server.Disconnect();

                    continue;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (this.OnException == null)
                        throw ex;

                    this.OnException.Invoke(this, new ThreadExceptionEventArgs(ex));
                    break;
                }
            }
        }

        /// <summary>
        /// Waits for the main process to close for a specified time (Does not work correctly on connections from other users).
        /// </summary>
        /// <param name="millisecondsTimeout">maximum waiting time.</param>
        /// <returns>True if the current process is identified as the main one.</returns>
        private bool Wait(int millisecondsTimeout)
        {
            try
            {
                return this._mutex.WaitOne(millisecondsTimeout);
            }
            catch (AbandonedMutexException)
            {
                return true;
            }
        }

        //Read connection packet
        private async Task<byte[]> ReadPacketAsync(int len, CancellationToken token)
        {
            var packet = new byte[len];
            await this._server.ReadAsync(packet, 0, len, token);
            return packet;
        }

        #endregion
    }
}