﻿using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using Google.Protobuf;
using Microsoft.Extensions.Hosting;
using Reptile;
using SlopCrew.Common;
using SlopCrew.Common.Proto;
using UnityEngine;

namespace SlopCrew.Plugin;

public class SlopConnectionManager : IHostedService {
    public ulong ServerTick;
    public ulong Latency;
    
    private ManualLogSource logger;

    private NetworkingSockets client;
    private uint? connection = null;
    private Address address;
    private ConnectionState lastState = ConnectionState.None;

    public Action<ClientboundMessage>? MessageReceived;
    public Action? Tick;

    private float? tickRate = null;
    private float tickTimer = 0;

    public SlopConnectionManager(ManualLogSource logger) {
        this.logger = logger;

        Library.Initialize();
        this.client = new NetworkingSockets();

        this.address = new Address();
        this.address.SetAddress("::1", 42069);

        Core.OnUpdate += this.Update;
    }

    public Task StartAsync(CancellationToken cancellationToken) {
        this.Connect();
        return Task.CompletedTask;
    }

    public void Connect() {
        this.connection = this.client.Connect(ref address);
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        Core.OnUpdate -= this.Update;

        if (this.connection is not null) this.client.CloseConnection(this.connection.Value);
        Library.Deinitialize();

        return Task.CompletedTask;
    }

    private void Update() {
        if (this.tickRate is not null) {
            this.tickTimer += Time.deltaTime;
            if (this.tickTimer >= this.tickRate) {
                this.tickTimer -= this.tickRate.Value;
                this.Tick?.Invoke();
            }
        }

        this.client.RunCallbacks();
        if (this.connection == null) return;

        // The callbacks just crash. I don't know why they do.
        // This works. Free me. ~NotNite
        var info = new ConnectionInfo();
        this.client.GetConnectionInfo(this.connection.Value, ref info);
        if (info.state != this.lastState) {
            this.logger.LogInfo($"Connection state changed from {this.lastState} to {info.state}");
            this.lastState = info.state;
            this.HandleStateChange(info);
        }

        const int maxMessages = 20;
        var messages = new NetworkingMessage[maxMessages];

        var count = this.client.ReceiveMessagesOnConnection(this.connection!.Value, messages, maxMessages);
        if (count > 0) {
            for (var i = 0; i < count; i++) {
                ref var netMessage = ref messages[i];
                var data = new byte[netMessage.length];
                Marshal.Copy(netMessage.data, data, 0, netMessage.length);

                var packet = ClientboundMessage.Parser.ParseFrom(data);
                if (packet is not null) this.ProcessMessage(packet);

                netMessage.Destroy();
            }
        }
    }

    private void ProcessMessage(ClientboundMessage packet) {
        this.MessageReceived?.Invoke(packet);

        switch (packet.MessageCase) {
            case ClientboundMessage.MessageOneofCase.Hello: {
                this.logger.LogInfo($"Received hello packet with tick rate {packet.Hello.TickRate}");
                this.tickRate = 1 / packet.Hello.TickRate;
                break;
            }
        }
    }

    public void SendMessage(ServerboundMessage packet, SendFlags flags = SendFlags.Reliable) {
        if (this.connection is null) throw new Exception("Not connected");
        var bytes = packet.ToByteArray();
        this.client.SendMessageToConnection(this.connection.Value, bytes, flags);
    }

    private void HandleStateChange(ConnectionInfo connectionInfo) {
        switch (connectionInfo.state) {
            case ConnectionState.Connected: {
                this.SendMessage(new ServerboundMessage {
                    Version = new ServerboundVersion {
                        ProtocolVersion = Constants.NetworkVersion,
                        PluginVersion = PluginInfo.PLUGIN_VERSION
                    }
                });
                break;
            }

            case ConnectionState.ClosedByPeer:
            case ConnectionState.ProblemDetectedLocally: {
                this.OnDisconnect();
                break;
            }
        }
    }

    private void OnDisconnect() {
        this.logger.LogWarning("Disconnected - attempting to reconnect");

        this.tickRate = null;
        this.tickTimer = 0;

        this.client.CloseConnection(this.connection!.Value);
        Task.Delay(5000).ContinueWith(_ => this.Connect());
    }
}
