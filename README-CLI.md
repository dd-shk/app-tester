# 🖥️ FlowRunner CLI - Phase 1 Testing Guide

## ✅ مرحله اول تکمیل شد!

یک **Command Line Interface** برای اجرای خودکار flow ها اضافه شد.

---

## 📦 فایل‌های جدید:

```
FlowRunner.CLI/
├── FlowRunner.CLI.csproj          # پروژه Console
├── Program.cs                      # Entry point
├── CommandLineOptions.cs           # Parser آرگومان‌ها
├── CliRunner.cs                    # مدیریت اجرا
├── HeadlessFlowExecutor.cs         # اجراکننده headless
└── ExitCodes.cs                    # کدهای خروجی
```

---

## 🔨 نحوه Build:

### روش 1: Visual Studio
1. باز کن `Auto Click.sln`
2. پروژه `FlowRunner.CLI` رو ببین در Solution Explorer
3. Build کن (Ctrl+Shift+B)

### روش 2: Command Line
```bash
cd FlowRunner.CLI
dotnet build
```

---

## 🧪 نحوه تست:

### مرحله 1: آماده‌سازی
1. برنامه GUI رو باز کن (`Auto Click`)
2. یک Flow ساده بساز:
   - Category: "Test"
   - Name: "SimpleTest"
   - چند کلیک ساده اضافه کن
3. Save کن

### مرحله 2: تست CLI

#### 📋 نمایش راهنما:
```bash
cd bin\Debug\net8.0-windows
FlowRunner.CLI.exe --help
```

**انتظار:** باید راهنمای استفاده رو نشون بده

---

#### 📋 لیست Flow ها:
```bash
FlowRunner.CLI.exe --list
```

**انتظار:** باید لیست تمام flow های ذخیره شده رو نشون بده  
**مثال خروجی:**
```
=== Available Flows ===

📁 Category: Test
   ✓ SimpleTest

📁 Category: General
   ✓ MyFlow
```

---

#### ▶️ اجرای Flow:
```bash
FlowRunner.CLI.exe --category "Test" --flow "SimpleTest"
```

**انتظار:**
- Flow اجرا بشه
- ماوس حرکت کنه و کلیک کنه
- خروجی موفقیت نشون بده

**مثال خروجی موفق:**
```
🚀 Running flow: Test/SimpleTest
   Loops: 1

📝 Flow loaded: 5 steps
⏱️  Starting execution...

=== Execution Results ===
Status: ✅ SUCCESS
Duration: 1234ms
Steps Executed: 5/5
```

---

#### ▶️ اجرا با Verbose:
```bash
FlowRunner.CLI.exe -c "Test" -f "SimpleTest" --verbose
```

**انتظار:** باید جزئیات هر step رو نشون بده

---

#### 🔁 اجرا با Loop:
```bash
FlowRunner.CLI.exe -c "Test" -f "SimpleTest" --loops 3
```

**انتظار:** Flow سه بار تکرار بشه

---

### مرحله 3: تست Exit Codes

#### در PowerShell:
```powershell
FlowRunner.CLI.exe -c "Test" -f "SimpleTest"
echo "Exit Code: $LASTEXITCODE"
```

#### در CMD:
```cmd
FlowRunner.CLI.exe -c "Test" -f "SimpleTest"
echo Exit Code: %ERRORLEVEL%
```

**Exit Codes:**
- `0` = موفقیت ✅
- `1` = Flow فیل شد
- `2` = خطا در اجرا
- `3` = آرگومان‌های نامعتبر
- `4` = Flow پیدا نشد

---

## ✅ Checklist تست:

قبل از رفتن به Phase 2، این‌ها رو چک کن:

- [ ] Build بدون خطا
- [ ] `--help` راهنما رو نشون میده
- [ ] `--list` لیست flow ها رو نشون میده
- [ ] اجرای یک flow ساده موفق هست
- [ ] `--verbose` جزئیات نشون میده
- [ ] `--loops` کار می‌کنه
- [ ] Exit code صحیح برمی‌گردونه
- [ ] Flow نامعتبر خطای مناسب میده

---

## 🐛 مشکلات احتمالی:

### ❌ "Flow not found"
**حل:** مطمئن شو flow رو از GUI ذخیره کردی

### ❌ Build error
**حل:** مطمئن شو .NET 8 SDK نصب هست

### ❌ Mouse حرکت نمی‌کنه
**حل:** برنامه رو با Admin rights اجرا کن

---

## 📞 بعد از تست:

اگه همه چیز کار کرد، بگو:
- ✅ **"همه چیز درسته، بریم Phase 2"**

اگه مشکل داشت، بگو:
- ❌ **"این قسمت مشکل داره: [توضیح مشکل]"**

---

## 🎯 بعدی چیه؟

**Phase 2:** JSON Output & Reporting
- خروجی نتایج به JSON
- ذخیره screenshot ها
- گزارش‌دهی پیشرفته
