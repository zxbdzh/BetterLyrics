using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BetterLyrics.Core.Messages.AppSettings;

public sealed class OhMyGoMusicServerChangedMessage(string value) : ValueChangedMessage<string>(value);
