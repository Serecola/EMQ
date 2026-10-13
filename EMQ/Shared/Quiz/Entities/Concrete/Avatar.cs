using System;
using System.Collections.Generic;
using EMQ.Shared.Core;

namespace EMQ.Shared.Quiz.Entities.Concrete;

public class Avatar
{
    public Avatar(AvatarCharacter character, string skin = "Default")
    {
        Character = character;
        Skin = skin;
    }

    public AvatarCharacter Character { get; set; }

    public string Skin { get; set; }

    public static Dictionary<AvatarCharacter, List<string>> SkinsDict { get; } = new()
    {
        { AvatarCharacter.Auu, new List<string> { "Default", "OG" } },
        { AvatarCharacter.VNDBCharacterImage, new List<string> { "" } },
        { AvatarCharacter.ProcrasAndTina, new List<string> { "Default" } },
        { AvatarCharacter.MALCharacterImage, new List<string> { "" } },
        { AvatarCharacter.UploadedImage, new List<string> { "" } },
    };

    public static Avatar DefaultAvatar { get; } = new(AvatarCharacter.Auu, "Default");

    public static string GetUrlByPlayerState(Avatar? avatar, PlayerStatus playerStatus)
    {
        string pose = playerStatus switch
        {
            PlayerStatus.Default => "default",
            PlayerStatus.Thinking => "thinking",
            PlayerStatus.Guessed => "guessed",
            PlayerStatus.Correct => "correct",
            PlayerStatus.Wrong => "wrong",
            PlayerStatus.Dead => "wrong",
            PlayerStatus.Looting => "looting",
            _ => "default"
        };

        if (avatar is null)
        {
            return $"assets/avatars/auu/default/{pose}.webp".ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(avatar.Skin))
        {
            if (avatar.Character is AvatarCharacter.VNDBCharacterImage)
            {
                (string modStr, int number) = Utils.ParseVndbScreenshotStr(avatar.Skin);
                return $"/selfhoststorage/vndb-img/ch/{modStr}/{number}.jpg";
            }

            string? databaseStr = null;
            string? extensionStr = null;
            switch (avatar.Character)
            {
                case AvatarCharacter.MALCharacterImage:
                    databaseStr = "mal-img";
                    extensionStr = "webp";
                    break;
                case AvatarCharacter.UploadedImage:
                    return Utils.FormatNekobakoLink($"{avatar.Skin}.webp");
            }

            if (databaseStr != null)
            {
                (string modStr, int number) = Utils.ParseVndbScreenshotStr(avatar.Skin);
                return $"{Constants.WebsiteDomain}/selfhoststorage/{databaseStr}/ch/{modStr}/{number}.{extensionStr}";
            }
        }

        return $"assets/avatars/{avatar.Character.ToString()}/{avatar.Skin}/{pose}.webp".ToLowerInvariant();
    }
}
