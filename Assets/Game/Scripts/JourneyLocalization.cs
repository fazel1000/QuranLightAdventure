using System;
using UnityEngine;

// Language IDs preserve JourneyMenuController's existing saved preferences.
public static class JourneyLocalization
{
    public static event Action Changed;
    public static int Language => Mathf.Clamp(PlayerPrefs.GetInt("JourneyOfLight.Language", 0), 0, 2);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { Changed = null; }
    public static void SetLanguage(int language)
    {
        PlayerPrefs.SetInt("JourneyOfLight.Language", Mathf.Clamp(language, 0, 2));
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
    public static string Choose(string persian, string english, string arabic)
    {
        string selected = Language == 1 ? english : Language == 2 ? arabic : persian;
        return string.IsNullOrEmpty(selected) ? persian : selected;
    }
    public static string FormatPercent(float score)
    {
        string text = Mathf.FloorToInt(Mathf.Clamp(score, 0, 100)).ToString(global::System.Globalization.CultureInfo.InvariantCulture);
        if (Language != 1)
        {
            char zero = Language == 2 ? '٠' : '۰';
            for (int i = 0; i < 10; i++) text = text.Replace((char)('0' + i), (char)(zero + i));
        }
        return text + (Language == 1 ? "%" : "٪");
    }
    public static void WritePercent(TMPro.TMP_Text target, float score)
    {
        if (target == null) return;
        if (target is RTLTMPro.RTLTextMeshPro rtl) rtl.PreserveNumbers = true;
        if (target is RTLTMPro.RTLTextMeshPro3D rtl3d) rtl3d.PreserveNumbers = true;
        JourneyLocalizedText.Write(target, FormatPercent(score));
    }
    private static readonly string[][] Entries = {
        new[] { "لطفاً کریستال این سوره را پیدا کن و در جای خود قرار بده", "Find this surah's crystal and place it in its socket", "اعثر على بلورة هذه السورة وضعها في مكانها" },
        new[] { "کریستال پیدا شد؛ نزدیک تابلو برو و جای کریستال را لمس کن", "Crystal found; approach the board and tap the crystal socket", "عُثر على البلورة؛ اقترب من اللوحة والمس مكان البلورة" },
        new[] { "اول جعبه‌های کلمات این آیه را پیدا کن و درست به هم وصل کن", "First find and correctly connect this verse's word boxes", "اعثر أولًا على صناديق كلمات هذه الآية وصِلْها بالترتيب الصحيح" },
        new[] { "شخصی‌سازی", "Customize", "تخصيص الشخصية" },
        new[] { "پرش", "Jump", "قفز" },
        new[] { "شروع", "Start", "ابدأ" },
        new[] { "شروع بازی", "Start game", "ابدأ اللعب" },
        new[] { "ادامه", "Continue", "متابعة" },
        new[] { "تنظیمات", "Settings", "الإعدادات" },
        new[] { "خروج", "Exit", "خروج" },
        new[] { "بازگشت", "Back", "رجوع" },
        new[] { "برگشت", "Back", "رجوع" },
        new[] { "پخش", "Play", "تشغيل" },
        new[] { "ضبط", "Record", "تسجيل" },
        new[] { "توقف", "Stop", "إيقاف" },
        new[] { "پرتاب", "Launch", "إطلاق" },
        new[] { "حمله", "Attack", "هجوم" },
        new[] { "دفاع", "Defend", "دفاع" },
        new[] { "سوار شدن", "Enter vehicle", "ركوب المركبة" },
        new[] { "پیاده شدن", "Exit vehicle", "مغادرة المركبة" },
        new[] { "گاز", "Accelerate", "تسارع" },
        new[] { "ترمز", "Brake", "فرامل" },
        new[] { "نیترو", "Nitro", "نيترو" },
        new[] { "صدای موسیقی", "Music volume", "مستوى صوت الموسيقى" },
        new[] { "موسیقی", "Music", "الموسيقى" },
        new[] { "آهنگ بعدی", "Next track", "المقطع التالي" },
        new[] { "آهنگ قبلی", "Previous track", "المقطع السابق" },
        new[] { "زبان", "Language", "اللغة" },
        new[] { "حرکت با کشیدن انگشت", "Move by dragging", "تحرك بالسحب" },
        new[] { "دکمه", "Button", "زر" },
        new[] { "آمادهٔ ضبط", "Ready to record", "جاهز للتسجيل" },
        new[] { "مدل هنوز آماده نیست", "Model is not ready yet", "النموذج غير جاهز بعد" },
        new[] { "در حال آماده شدن مدل…", "Preparing model…", "جارٍ تجهيز النموذج…" },
        new[] { "در حال آماده شدن…", "Preparing…", "جارٍ التجهيز…" },
        new[] { "در حال آماده‌سازی فایل‌های مدل روی دستگاه…", "Preparing model files…", "جارٍ تجهيز ملفات النموذج…" },
        new[] { "در حال راه‌اندازی موتور تشخیص صدا…", "Initializing speech recognition…", "جارٍ تهيئة التعرف على الكلام…" },
        new[] { "ضبط لغو شد", "Recording cancelled", "تم إلغاء التسجيل" },
        new[] { "گوش کن و سپس بخوان", "Listen, then recite", "استمع ثم اقرأ" },
        new[] { "اجازهٔ دسترسی به میکروفون لازم است", "Microphone permission is required", "يلزم السماح باستخدام الميكروفون" },
        new[] { "در حال ضبط… برای پایان دوباره بزن", "Recording… tap again to finish", "جارٍ التسجيل… اضغط مجددًا للإنهاء" },
        new[] { "میکروفون در دسترس نیست", "Microphone unavailable", "الميكروفون غير متاح" },
        new[] { "ضبط شروع نشد", "Recording could not start", "تعذر بدء التسجيل" },
        new[] { "صدایی ضبط نشد؛ دوباره تلاش کن", "No audio recorded; try again", "لم يُسجّل صوت؛ حاول مجددًا" },
        new[] { "در حال بررسی…", "Checking…", "جارٍ التقييم…" },
        new[] { "بررسی انجام شد", "Assessment complete", "اكتمل التقييم" },
        new[] { "بررسی انجام نشد؛ دوباره تلاش کن", "Assessment failed; try again", "تعذر التقييم؛ حاول مجددًا" },
        new[] { "آیهٔ تابلو در فهرست سوره نیست یا شمارهٔ تکراری دارد", "Board verse is missing or duplicated", "آية اللوحة مفقودة أو رقمها مكرر" },
        new[] { "صدای این آیه را در فهرست سوره قرار بده", "Assign audio for this verse", "أضف الصوت لهذه الآية" },
        new[] { "خطای آماده‌سازی مدل: ", "Model preparation error: ", "خطأ في تجهيز النموذج: " },
        new[] { "خطای مدل: ", "Model error: ", "خطأ في النموذج: " },
        new[] { "در حال استخراج مدل از فایل بازی: ", "Extracting bundled model: ", "جارٍ استخراج النموذج: " },
    };
    public static bool TryGet(string source, out string persian, out string english, out string arabic)
    {
        foreach (var row in Entries)
            if (source == row[0] || source == row[1] || source == row[2])
            { persian = row[0]; english = row[1]; arabic = row[2]; return true; }
        persian = source; english = arabic = ""; return false;
    }
    public static string Translate(string source)
    {
        if (string.IsNullOrEmpty(source)) return source;
        if (TryGet(source, out var fa, out var en, out var ar)) return Choose(fa, en, ar);
        foreach (var row in Entries)
            if (row[0].EndsWith(": ", StringComparison.Ordinal) && source.StartsWith(row[0], StringComparison.Ordinal))
                return Choose(row[0], row[1], row[2]) + source.Substring(row[0].Length);
        // Never guess at unknown text, Quran verses, or technical errors.
        return source;
    }
}
