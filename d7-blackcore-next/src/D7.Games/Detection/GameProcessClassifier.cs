namespace D7.Games.Detection;

public sealed class GameProcessClassifier
{
    private static readonly HashSet<string> NeverGameNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "obs64", "obs32", "streamlabs", "mediasdk_server", "discord", "steam", "steamwebhelper",
        "battle.net", "agent", "eadesktop", "epicgameslauncher", "epicwebhelper", "nvidia app",
        "nvidia share", "nvcontainer", "msiafterburner", "rtss", "python", "pythonw", "node",
        "code", "devenv", "powershell", "pwsh", "cmd", "windowsterminal", "explorer", "taskmgr",
        "searchapp", "searchhost", "startmenuexperiencehost", "shellexperiencehost",
        "applicationframehost", "d7.blackcore", "d7agent", "cua-driver", "freedownloadmanager"
    };

    private static readonly string[] NeverGamePathFragments =
    [
        @"\obs-studio\", @"\tiktok live studio\", @"\nvidia corporation\",
        @"\microsoft vs code\", @"\visual studio\", @"\python\", @"\nodejs\",
        @"\d7 agent\", @"\d7 blackcore\", @"\appdata\local\discord\",
        @"\battle.net\battle.net", @"\epic games\launcher\"
    ];

    private static readonly string[] PositiveGamePathFragments =
    [
        @"\steamapps\common\", @"\xboxgames\", @"\epic games\",
        @"\ea games\", @"\games\call of duty\", @"\gm\call of duty\"
    ];

    public GameDetectionDecision Classify(GameProcessFacts facts)
    {
        var evidence = new List<string>();
        var rejections = new List<string>();
        var name = NormalizeName(facts.ProcessName);
        var path = (facts.ExecutablePath ?? string.Empty).ToLowerInvariant();

        if (NeverGameNames.Contains(name) || NeverGamePathFragments.Any(path.Contains))
        {
            rejections.Add("العملية مصنفة كأداة بث أو نظام أو لانشر أو تطوير وليست لعبة.");
            return new GameDetectionDecision(false, -100, "عالٍ", evidence, rejections);
        }

        var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(windowsPath) && path.StartsWith(windowsPath, StringComparison.OrdinalIgnoreCase))
        {
            rejections.Add("العملية من مسار نظام Windows.");
            return new GameDetectionDecision(false, -100, "عالٍ", evidence, rejections);
        }

        var score = 0;
        if (facts.IsLearned)
        {
            score += 70;
            evidence.Add("اللعبة محفوظة سابقًا في ملف موثوق.");
        }
        if (facts.IsKnownExecutable)
        {
            score += 75;
            evidence.Add("اسم الملف التنفيذي موجود في كتالوج ألعاب موثوق داخل D7.");
        }
        if (facts.IsKnownGameInstallPath || PositiveGamePathFragments.Any(path.Contains))
        {
            score += 50;
            evidence.Add("المسار يتوافق مع مكتبة ألعاب معروفة.");
        }
        if (facts.IsForeground)
        {
            score += 20;
            evidence.Add("العملية في الواجهة الأمامية.");
        }
        if (facts.HasGraphicsActivity)
        {
            score += 25;
            evidence.Add("توجد إشارة نشاط رسومي للعملية.");
        }
        if (string.IsNullOrWhiteSpace(facts.ExecutablePath))
        {
            score -= 15;
            rejections.Add("مسار الملف التنفيذي غير متاح، لذلك خُفضت الثقة.");
        }

        var isGame = score >= 60;
        var confidence = score >= 90 ? "عالٍ" : score >= 60 ? "متوسط" : "منخفض";
        if (!isGame) rejections.Add("الأدلة الحالية غير كافية لتصنيف العملية كلعبة.");
        return new GameDetectionDecision(isGame, score, confidence, evidence, rejections);
    }

    private static string NormalizeName(string processName)
    {
        var name = processName.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
