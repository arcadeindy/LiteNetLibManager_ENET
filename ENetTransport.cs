﻿using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using LiteNetLibManager;
using ENet;

public class ENetTransport : ITransport
{
    private Host client;
    private Peer clientPeer;
    private Host server;
    private readonly Dictionary<long, Peer> serverPeers;
    // Improve garbage collection
    private Event tempNetEvent;
    private byte[] tempBuffers;

    public ENetTransport()
    {
        serverPeers = new Dictionary<long, Peer>();
    }

    public bool IsClientStarted()
    {
        return clientPeer.IsSet && clientPeer.State == PeerState.Connected;
    }

    public bool StartClient(string connectKey, string address, int port)
    {
        client = new Host();
        Address addressData = new Address();
        addressData.SetHost(address);
        addressData.Port = (ushort)port;
        client.Create();
        clientPeer = client.Connect(addressData, 4);
        return clientPeer.IsSet;
    }

    public void StopClient()
    {
        if (clientPeer.IsSet)
            clientPeer.Disconnect(0);
        if (client != null)
            client.Dispose();
        client = null;
    }

    public bool ClientReceive(out TransportEventData eventData)
    {
        eventData = default(TransportEventData);
        if (client == null)
            return false;
        client.Service(0, out tempNetEvent);
        switch (tempNetEvent.Type)
        {
            case EventType.None:
                return false;

            case EventType.Connect:
                eventData.type = ENetworkEvent.ConnectEvent;
                eventData.connectionId = tempNetEvent.Peer.ID;
                break;

            case EventType.Disconnect:
                eventData.type = ENetworkEvent.DisconnectEvent;
                eventData.connectionId = tempNetEvent.Peer.ID;
                break;

            case EventType.Timeout:
                eventData.type = ENetworkEvent.DisconnectEvent;
                eventData.connectionId = tempNetEvent.Peer.ID;
                eventData.disconnectInfo = new DisconnectInfo()
                {
                    Reason = DisconnectReason.Timeout
                };
                break;

            case EventType.Receive:
                tempBuffers = new byte[tempNetEvent.Packet.Length];
                tempNetEvent.Packet.CopyTo(tempBuffers);

                eventData.type = ENetworkEvent.DataEvent;
                eventData.connectionId = tempNetEvent.Peer.ID;
                eventData.reader = new NetDataReader(tempBuffers);
                tempNetEvent.Packet.Dispose();
                break;
        }
        return true;
    }

    public bool ClientSend(SendOptions sendOptions, NetDataWriter writer)
    {
        if (IsClientStarted())
        {
            Packet packet = default(Packet);
            packet.Create(writer.Data, writer.Length, GetPacketFlags(sendOptions));
            clientPeer.Send(GetChannelID(sendOptions), ref packet);
            return true;
        }
        return false;
    }

    public bool IsServerStarted()
    {
        return server != null && server.IsSet;
    }

    public bool StartServer(string connectKey, int port, int maxConnections)
    {
        serverPeers.Clear();
        server = new Host();
        Address address = new Address();
        address.Port = (ushort)port;
        server.Create(address, maxConnections, 4);
        return true;
    }

    public bool ServerReceive(out TransportEventData eventData)
    {
        eventData = default(TransportEventData);
        if (server == null)
            return false;
        server.Service(0, out tempNetEvent);
        switch (tempNetEvent.Type)
        {
            case EventType.None:
                return false;

            case EventType.Connect:
                eventData.type = ENetworkEvent.ConnectEvent;
                eventData.connectionId = tempNetEvent.Peer.ID;
                serverPeers[tempNetEvent.Peer.ID] = tempNetEvent.Peer;
                break;

            case EventType.Disconnect:
                eventData.type = ENetworkEvent.DisconnectEvent;
                eventData.connectionId = tempNetEvent.Peer.ID;
                serverPeers.Remove(tempNetEvent.Peer.ID);
                break;

            case EventType.Timeout:
                eventData.type = ENetworkEvent.DisconnectEvent;
                eventData.connectionId = tempNetEvent.Peer.ID;
                eventData.disconnectInfo = new DisconnectInfo()
                {
                    Reason = DisconnectReason.Timeout
                };
                serverPeers.Remove(tempNetEvent.Peer.ID);
                break;

            case EventType.Receive:
                tempBuffers = new byte[tempNetEvent.Packet.Length];
                tempNetEvent.Packet.CopyTo(tempBuffers);

                eventData.type = ENetworkEvent.DataEvent;
                eventData.connectionId = tempNetEvent.Peer.ID;
                eventData.reader = new NetDataReader(tempBuffers);
                tempNetEvent.Packet.Dispose();
                break;
        }
        return true;
    }

    public bool ServerSend(long connectionId, SendOptions sendOptions, NetDataWriter writer)
    {
        if (IsServerStarted() && serverPeers.ContainsKey(connectionId))
        {
            Packet packet = default(Packet);
            packet.Create(writer.Data, writer.Length, GetPacketFlags(sendOptions));
            serverPeers[connectionId].Send(GetChannelID(sendOptions), ref packet);
            return true;
        }
        return false;
    }

    public bool ServerDisconnect(long connectionId)
    {
        if (IsServerStarted() && serverPeers.ContainsKey(connectionId))
        {
            serverPeers[connectionId].Disconnect(0);
            return true;
        }
        return false;
    }

    public void StopServer()
    {
        if (server != null)
            server.Dispose();
        server = null;
    }

    public void Destroy()
    {
        StopClient();
        StopServer();
    }

    public int GetFreePort()
    {
        Socket socketV4 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socketV4.Bind(new IPEndPoint(IPAddress.Any, 0));
        int port = ((IPEndPoint)socketV4.LocalEndPoint).Port;
        socketV4.Close();
        return port;
    }

    public byte GetChannelID(SendOptions sendOptions)
    {
        switch (sendOptions)
        {
            case SendOptions.Sequenced:
            case SendOptions.Unreliable:
                return 1;
        }
        return 0;
    }

    public PacketFlags GetPacketFlags(SendOptions sendOptions)
    {
        switch (sendOptions)
        {
            case SendOptions.ReliableOrdered:
                return PacketFlags.Reliable;
            case SendOptions.ReliableUnordered:
                return PacketFlags.Reliable | PacketFlags.Unsequenced;
            case SendOptions.Sequenced:
                return PacketFlags.None;
            default:
                return PacketFlags.None | PacketFlags.Unsequenced;
        }
    }
}
