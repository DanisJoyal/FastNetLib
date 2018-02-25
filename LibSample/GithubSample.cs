﻿using System;
using System.Threading;
using FastNetLib;
using FastNetLib.Utils;

namespace LibSample
{
    class GithubSample
    {
        public void Client()
        {
            EventBasedNetListener listener = new EventBasedNetListener();
            NetManager server = new NetManager(listener, 2 /* maximum clients */);
            server.Start(9050 /* port */);

            listener.ConnectionRequestEvent += request =>
            {
                request.AcceptIfKey("SomeConnectionKey");
            };

            listener.PeerConnectedEvent += peer =>
            {
                Console.WriteLine("We got connection: {0}", peer.EndPoint); // Show peer ip
                NetDataWriter writer = new NetDataWriter();                 // Create writer class
                writer.Put("Hello client!");                                // Put some string
                peer.Send(writer, DeliveryMethod.ReliableOrdered, 0);             // Send with reliability
            };

            while (!Console.KeyAvailable)
            {
                server.Run(15);
            }

            server.Stop();
        }

        public void Server()
        {
            EventBasedNetListener listener = new EventBasedNetListener();
            NetManager client = new NetManager(listener);
            client.Start();
            client.Connect("localhost" /* host ip or name */, 9050 /* port */, "SomeConnectionKey" /* text key or NetDataWriter */);
            listener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod, channel) =>
            {
                Console.WriteLine("We got: {0}", dataReader.GetString(100 /* max length of string */));
            };

            while (!Console.KeyAvailable)
            {
                client.Run(15);
            }

            client.Stop();
        }
    }
}
