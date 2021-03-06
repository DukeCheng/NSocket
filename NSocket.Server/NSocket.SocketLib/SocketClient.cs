﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace NSocket.SocketLib
{
    public class SocketClient : IDisposable
    {
        /// <summary>
        /// 客户端连接Socket
        /// </summary>
        private Socket clientSocket;
        /// <summary>
        /// 连接状态
        /// </summary>
        public Boolean Connected { get { return this.clientSocket.Connected; } }
        /// <summary>
        /// 连接点
        /// </summary>
        private IPEndPoint hostEndPoint;
        /// <summary>
        /// 连接信号量
        /// </summary>
        private static AutoResetEvent autoConnectEvent = new AutoResetEvent(false);
        /// <summary>
        /// 接受到数据时的委托
        /// </summary>
        /// <param name="info"></param>
        public delegate void ReceiveMsgHandler(string message);
        /// <summary>
        /// 接收到数据时调用的事件
        /// </summary>
        public event ReceiveMsgHandler OnMsgReceived;
        /// <summary>
        /// 开始监听数据的委托
        /// </summary>
        public delegate void StartListenHandler();
        /// <summary>
        /// 开始监听数据的事件
        /// </summary>
        public event StartListenHandler StartListenThread;
        /// <summary>
        /// 发送信息完成的委托
        /// </summary>
        /// <param name="successorfalse"></param>
        public delegate void SendCompleted(bool successorfalse);
        /// <summary>
        /// 发送信息完成的事件
        /// </summary>
        public event SendCompleted OnSended;
        /// <summary>
        /// 监听接收的SocketAsyncEventArgs
        /// </summary>
        private SocketAsyncEventArgs listenerSocketAsyncEventArgs;

        public event Action<SocketError> ServerEvent;

        public event Action ServerStop;
        public event Action<byte[], int, int> DateReceivedEvent;
        public event Action<byte[], int, int> DataSendedEvent;

        private readonly int MESSAGE_BUFFER_SIZE = 1024;

        /// <summary>
        /// 初始化客户端
        /// </summary>
        /// <param name="address">服务端地址{IP地址}</param>
        /// <param name="port">端口号</param>
        public SocketClient(IPAddress address, Int32 port, int messageBuffer)
        {
            this.MESSAGE_BUFFER_SIZE = messageBuffer;
            this.hostEndPoint = new IPEndPoint(address, port);
            this.clientSocket = new Socket(this.hostEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        }

        #region Connect

        /// <summary>
        /// 连接服务端
        /// </summary>
        public bool Connect()
        {
            if (this.clientSocket != null)
            {
                this.clientSocket.Dispose();
            }
            this.clientSocket = new Socket(this.hostEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            SocketAsyncEventArgs connectArgs = new SocketAsyncEventArgs();
            connectArgs.UserToken = this.clientSocket;
            connectArgs.RemoteEndPoint = this.hostEndPoint;
            byte[] connectBuffer = new byte[MESSAGE_BUFFER_SIZE];
            connectArgs.SetBuffer(connectBuffer, 0, connectBuffer.Length);
            connectArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnConnect);
            clientSocket.ConnectAsync(connectArgs);

            autoConnectEvent.WaitOne(10000);//Waiting for the connect result.
            if (connectArgs.SocketError == SocketError.Success && this.clientSocket.Connected)
            {
                listenerSocketAsyncEventArgs = new SocketAsyncEventArgs();
                byte[] receiveBuffer = new byte[MESSAGE_BUFFER_SIZE];
                listenerSocketAsyncEventArgs.UserToken = clientSocket;
                listenerSocketAsyncEventArgs.SetBuffer(receiveBuffer, 0, receiveBuffer.Length);
                listenerSocketAsyncEventArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnReceive);
                Listen();
                return true;
            }
            else
                return false;
        }

        /// <summary>
        /// 连接的完成方法
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnConnect(object sender, SocketAsyncEventArgs e)
        {
            autoConnectEvent.Set();
        }

        #endregion

        #region Receive and Listen
        /// <summary>
        /// 开始监听线程的入口函数
        /// </summary>
        public void Listen()
        {
            var socket = listenerSocketAsyncEventArgs.UserToken as Socket;
            if (socket.Connected)
                socket.ReceiveAsync(listenerSocketAsyncEventArgs);
        }

        /// <summary>
        /// 接收的完成方法
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnReceive(object sender, SocketAsyncEventArgs e)
        {
            if (e.LastOperation != SocketAsyncOperation.Receive)
                return;
            //OnMsgReceived(string.Format("Server COMMAND: {0}", e.SocketError.ToString()));
            switch (e.SocketError)
            {
                case SocketError.Success:
                    Listen();
                    DateReceivedEvent(e.Buffer, e.Offset, e.BytesTransferred);
                    //string msg = Encoding.Unicode.GetString(e.Buffer, 0, e.BytesTransferred);
                    //OnMsgReceived(msg);
                    break;
                case SocketError.ConnectionReset:
                    ServerStop();
                    break;
                default:
                    ServerEvent(e.SocketError);
                    break;
            }
        }
        #endregion

        #region Send
        /// <summary>
        /// 发送信息
        /// </summary>
        /// <param name="message"></param>
        public void Send(byte[] sendBuffer)
        {
            if (this.Connected)
            {
                SocketAsyncEventArgs senderSocketAsyncEventArgs = new SocketAsyncEventArgs();
                senderSocketAsyncEventArgs.UserToken = this.clientSocket;
                senderSocketAsyncEventArgs.SetBuffer(sendBuffer, 0, sendBuffer.Length);
                senderSocketAsyncEventArgs.RemoteEndPoint = this.hostEndPoint;
                senderSocketAsyncEventArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnSend);
                clientSocket.SendAsync(senderSocketAsyncEventArgs);
                DataSendedEvent(sendBuffer, 0, sendBuffer.Length);
            }
            else
            {
                OnSended(false);
                //throw new SocketException((Int32)SocketError.NotConnected);
            }
        }

        /// <summary>
        /// 发送的完成方法
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnSend(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                OnSended(true);
            }
            else
            {
                OnSended(false);
                //this.ProcessError(e);
            }
        }

        #endregion

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            this.clientSocket.Close();
        }

        #region IDisposable Members
        public void Dispose()
        {
            autoConnectEvent.Close();
            if (this.clientSocket.Connected)
            {
                this.clientSocket.Close();
            }
        }
        #endregion
    }
}
