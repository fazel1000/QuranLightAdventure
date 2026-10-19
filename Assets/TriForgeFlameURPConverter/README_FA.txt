TriForge Flame → URP Converter (Unity 6)

این بسته برای متریال‌های زیر از پکیج TriForge Top Down Fantasy Forest آماده شده:
- M_Flame_Particle
- M_Flame_Particle_B

نصب:
1. از پروژه Unity بکاپ بگیر یا یک commit در Git ثبت کن.
2. پوشه TriForgeFlameURPConverter را داخل پوشه Assets پروژه کپی کن.
3. صبر کن Unity شیدر را Import و Compile کند.
4. از منوی Tools > TriForge > Flame URP Converter پنجره را باز کن.
5. روی Find Flame Materials بزن.
6. روی Convert Found Materials بزن.

این ابزار فقط متریال‌هایی را تبدیل می‌کند که نام دقیقشان M_Flame_Particle یا M_Flame_Particle_B باشد.
تکسچر اصلی، Tint HDR و پارامترهای Distortion/Dissolve را نگه می‌دارد و شیدر را به TriForge/Flame URP تغییر می‌دهد.

نکته‌ها:
- پروژه باید URP نصب و فعال داشته باشد.
- این یک پورت URP از منطق افکت است، نه تبدیل خودکار ۱۰۰٪ دقیق شیدر Amplify؛ ممکن است لازم باشد Brightness و پارامترهای افکت را در Inspector کمی تنظیم کنی.
- اگر depth fade درست دیده نشد، Depth Texture را در URP Asset یا تنظیمات Camera فعال کن.
- فایل‌های اصلی Flame.shader و متریال‌های اصلی را حذف نکن؛ این بسته فایل‌های جدید اضافه می‌کند و متریال‌ها را فقط وقتی دکمه Convert را بزنی تغییر می‌دهد.
