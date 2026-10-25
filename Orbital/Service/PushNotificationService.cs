using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace Orbital.Service;

public class PushNotificationService
{
    private readonly IConfiguration _config;

    public PushNotificationService(IConfiguration config)
    {
        _config = config;

        // Initialize Firebase only once
        if (FirebaseApp.DefaultInstance == null)
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "firebase-adminsdk.json");

            if (File.Exists(path))
            {
                FirebaseApp.Create(new AppOptions()
                {
                    Credential = GoogleCredential.FromFile(path)
                });
            }
            else
            {
                Console.WriteLine("⚠️ Firebase config missing at: " + path);
            }
        }
    }

    // 🔥 Updated to take 5 arguments (including notificationType)
    public async Task SendMessagePushAsync(string fcmToken, string title, string body, string chatId, string notificationType = "message")
    {
        if (string.IsNullOrEmpty(fcmToken) || FirebaseApp.DefaultInstance == null) return;

        var message = new Message()
        {
            Token = fcmToken,
            Notification = new Notification()
            {
                Title = title,
                Body = body
            },
            Data = new Dictionary<string, string>()
            {
                { "type", notificationType },
                { "chatId", chatId }
            },
            Android = new AndroidConfig()
            {
                Priority = Priority.High, // Wakes up the phone
                Notification = new AndroidNotification()
                {
                    Icon = "ic_notification", // 🔥 Uses your white logo (if added to android/app/src/main/res/drawable)
                    Sound = "default",
                    Tag = chatId,             // 🔥 Groups messages by user (like WhatsApp)
                    Color = "#D97706"         // Amber tint
                }
            }
        };

        try
        {
            await FirebaseMessaging.DefaultInstance.SendAsync(message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Push failed: {ex.Message}");
        }
    }

    // Keep your Call push method if you have it
    public async Task SendCallPushAsync(string fcmToken, string callerName, string sessionId)
    {
        if (string.IsNullOrEmpty(fcmToken) || FirebaseApp.DefaultInstance == null) return;

        var message = new Message()
        {
            Token = fcmToken,
            Data = new Dictionary<string, string>()
            {
                { "type", "incoming_call" },
                { "sessionId", sessionId },
                { "callerName", callerName }
            },
            Android = new AndroidConfig()
            {
                Priority = Priority.High,
                Notification = new AndroidNotification()
                {
                    Title = "Incoming Call",
                    Body = $"{callerName} is calling you...",
                    ChannelId = "calls",
                    Priority = NotificationPriority.MAX,
                    Sound = "ringtone"
                }
            }
        };

        try { await FirebaseMessaging.DefaultInstance.SendAsync(message); }
        catch (Exception ex) { Console.WriteLine($"Call push failed: {ex.Message}"); }
    }
}