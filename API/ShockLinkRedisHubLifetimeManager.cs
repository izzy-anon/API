﻿using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.Extensions.Options;

namespace ShockLink.API;

public class ShockLinkRedisHubLifetimeManager<THub> : RedisHubLifetimeManager<THub> where THub : Hub
{
    private readonly FieldInfo _usersField;
    private readonly FieldInfo _subscriptionsField;
    private readonly string _redisPrefix = typeof(THub).FullName!;

    public ShockLinkRedisHubLifetimeManager(ILogger<ShockLinkRedisHubLifetimeManager<THub>> logger,
        IOptions<RedisOptions> options, IHubProtocolResolver hubProtocolResolver,
        IOptions<HubOptions>? globalHubOptions,
        IOptions<HubOptions<THub>>? hubOptions) : base(logger, options, hubProtocolResolver, globalHubOptions,
        hubOptions)
    {
        _usersField = typeof(RedisHubLifetimeManager<THub>).GetField("_users", BindingFlags.NonPublic | BindingFlags.Instance)!;
        _subscriptionsField = _usersField.FieldType.GetField("_subscriptions", BindingFlags.NonPublic | BindingFlags.Instance)!;
    }

    public override Task SendUsersAsync(IReadOnlyList<string> userIds, string methodName, object?[] args,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count <= 0) return Task.CompletedTask;

        var message = new SerializedHubMessage(new InvocationMessage(methodName, args));

        var localUsers = new List<string>();
        var remoteUsers = new List<string>();

        foreach (var userId in userIds)
            if (userId.StartsWith("local#")) localUsers.Add(userId);
            else remoteUsers.Add(userId[6..]);
        var tasks = new List<Task> { base.SendUsersAsync(remoteUsers, methodName, args, cancellationToken) };
        // Do not close allocate here, just foreach :3
        // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
        foreach (var localUser in localUsers) tasks.Add(SendLocalMessageToUser(localUser, message, cancellationToken));

        return Task.WhenAll(tasks);
    }

    public override Task SendUserAsync(string userId, string methodName, object?[] args,
        CancellationToken cancellationToken = default)
    {
        return !userId.StartsWith("local#")
            ? base.SendUserAsync(userId, methodName, args, cancellationToken)
            : SendLocalMessageToUser(userId, methodName, args, cancellationToken);
    }
    private Task SendLocalMessageToUser(string userId, string methodName, object?[] args,
        CancellationToken cancellationToken)
    {
        var message = new SerializedHubMessage(new InvocationMessage(methodName, args));
        return SendLocalMessageToUser(userId, message, cancellationToken);
    }

    private Task SendLocalMessageToUser(string userId, SerializedHubMessage message,
        CancellationToken cancellationToken)
    {
        var users = _usersField.GetValue(this)!;
        var subsObj =
            (ConcurrentDictionary<string, HubConnectionStore>)_subscriptionsField.GetValue(users)!;

        if (!subsObj.TryGetValue($"{_redisPrefix}:user:{userId[6..]}", out var store)) return Task.CompletedTask;

        var tasks = new List<Task>(store.Count);
        foreach (var context in store) tasks.Add(context.WriteAsync(message, cancellationToken).AsTask());
        return Task.WhenAll(tasks);
    }
}