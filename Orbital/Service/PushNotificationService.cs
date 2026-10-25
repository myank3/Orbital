using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Threading.Tasks;
using System;

namespace Orbital.Service;

public class PushNotificationService
{
    private readonly IConfiguration _config;

    public PushNotificationService(IConfiguration config)
    {
        _config = config;

        // Initialize Firebase once when the app starts
        if (FirebaseApp.DefaultInstance == null)
        {
            // 🔥 Must point to the App_Data folder for MonsterASP
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
                // Log warning: Firebase JSON not found
                Console.WriteLine("⚠️ Firebase config missing at: " + path);
            }
        }
    }

    public async Task SendMessagePushAsync(string fcmToken, string title, string body, string chatId)
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
            Data = new System.Collections.Generic.Dictionary<string, string>()
            {
                { "type", "message" },
                { "chatId", chatId }
            },
            Android = new AndroidConfig()
            {
                Priority = Priority.High, // Wakes up the phone even in Doze mode
                Notification = new AndroidNotification()
                {
                    Sound = "default"
                }
            }
        };

        try
        {
            // Send to Firebase
            await FirebaseMessaging.DefaultInstance.SendAsync(message);
        }
        catch (Exception ex)
        {
            // Silently fail if token is invalid/expired so the API doesn't crash
            Console.WriteLine($"Push failed: {ex.Message}");
        }

    }
    public async Task SendCallPushAsync(string fcmToken, string callerName, string sessionId)
    {
        if (string.IsNullOrEmpty(fcmToken) || FirebaseApp.DefaultInstance == null) return;

        var message = new Message()
        {
            Token = fcmToken,
            // For calls, we often use Data-only payloads so the app can trigger a custom ringing UI
            Data = new System.Collections.Generic.Dictionary<string, string>()
        {
            { "type", "incoming_call" },
            { "sessionId", sessionId },
            { "callerName", callerName }
        },
            Android = new AndroidConfig()
            {
                Priority = Priority.High, // Highest priority to wake up the phone
                Notification = new AndroidNotification()
                {
                    Title = "Incoming Call",
                    Body = $"{callerName} is calling you...",
                    ChannelId = "calls", // You'll need to create this channel on the Android side
                    Priority = NotificationPriority.MAX,
                    Sound = "ringtone" // Use a custom ringtone
                }
            }
        };

        try { await FirebaseMessaging.DefaultInstance.SendAsync(message); }
        catch (Exception ex) { Console.WriteLine($"Call push failed: {ex.Message}"); }
    }
}