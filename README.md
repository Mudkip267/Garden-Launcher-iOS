<div align="center">
  <img src="assets/Garden-Launcher-Icon.png" alt="Garden Launcher" width="104" />
  <h1>Garden Launcher</h1>
  <p><b>Launcher &amp; Mod Manager cho Stardew Valley PC 1.6.15 trên iOS &amp; iPadOS</b></p>

  <p>
    <a href="https://github.com/hanprm9-dot/Garden-Launcher-iOS/releases/tag/v1.0.1"><img src="https://img.shields.io/badge/Version-v1.0.1-2ea44f?style=flat-square" alt="Version v1.0.1" /></a>
    <img src="https://img.shields.io/badge/iOS%20%2F%20iPadOS-14%20–%2027+-007aff?style=flat-square&logo=apple&logoColor=white" alt="iOS & iPadOS 14-27+" />
    <img src="https://img.shields.io/badge/SMAPI-4.5.2-6f42c1?style=flat-square" alt="SMAPI 4.5.2" />
    <img src="https://img.shields.io/badge/Runtime-Hybrid%20AOT%20(0%25%20JIT)-f59e0b?style=flat-square" alt="Hybrid AOT 0% JIT" />
    <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-64748b?style=flat-square" alt="MIT License" /></a>
  </p>
</div>

> [!NOTE]
> **Launcher sạch (0% tệp bản quyền):** Kho mã nguồn và các bản `.ipa` không chứa dữ liệu game của ConcernedApe. Bạn cần tự chuẩn bị bộ dữ liệu **Stardew Valley PC 1.6.15** của riêng mình.

---

## 📥 Tải xuống (`v1.0.1`)

| Thiết bị | Phiên bản IPA | Tải về |
| :--- | :--- | :---: |
| **iOS / iPadOS 14 – 18** | `Garden-Launcher-1.0.1-iOS14-18.ipa` | [**⬇️ Tải ngay**](https://github.com/hanprm9-dot/Garden-Launcher-iOS/releases/download/v1.0.1/Garden-Launcher-1.0.1-iOS14-18.ipa) |
| **iOS / iPadOS 26 – 27+** | `Garden-Launcher-1.0.1-iOS26-27.ipa` | [**⬇️ Tải ngay**](https://github.com/hanprm9-dot/Garden-Launcher-iOS/releases/download/v1.0.1/Garden-Launcher-1.0.1-iOS26-27.ipa) |

---

## ✨ Tính năng nổi bật trong `v1.0.1`

- **Hybrid AOT + Interpreter (`0% JIT`, biên dịch sẵn 27 thư viện lõi ARM64)** — Tăng tốc khởi động, giảm giật khung hình và tải CPU rõ rệt trên iPhone/iPad không cần Jailbreak hay bật JIT, vẫn giữ đầy đủ khả năng nạp mod SMAPI động 100%.
- **Màn hình Loading Pixel Art 16-bit thời gian thực** — Thanh tiến độ RPG chuyển sắc có **Junimo pixel nhảy theo mép thanh**, dải **5 mốc giai đoạn** (`.NET · SMAPI · Mods · Nội dung · Vào Game`), bầu trời sao + ánh nắng + mây/chim pixel, bảng **RAM live** và hiển thị trực tiếp **từng Mod, Content Pack & nhóm tài nguyên XNB** đang nạp trên Thread 0 (hết hẳn lỗi đứng `6%`), tự giải phóng `100%` RAM ngay khi vào màn hình Tiêu đề.
- **Hỗ trợ Full Ngoại vi (iOS / iPadOS 14–27+)** — Hoạt động nguyên bản qua `GameController.framework`: **Tay cầm rời** (Xbox / PlayStation / Switch Pro / Backbone / MFi đầy đủ analog, trigger, L3/R3 & điều khiển mượt cả trong Menu/Shop/Túi đồ), **Bàn phím vật lý** (`WASD`, `Esc`, `F1–F12`, mọi phím tắt mod SMAPI + gõ chữ trực tiếp vào Chat/Đặt tên), **Chuột & Trackpad** (di trỏ chuột PC, trái/phải/giữa, cuộn con lăn, lọc `UITouchTypeIndirectPointer` chống double-click trên iPadOS) và **Apple Pencil**; chạm tay lên màn hình tự chuyển về cảm ứng tức thì.
- **Đại tu chế độ Chạm đi (Tap-to-Move A\*), 2 Nút Pixel & Chế độ Tiết kiệm pin `60 / 45 / 30 FPS`** — 4 chế độ điều khiển chuẩn Mobile (*Vô hình*, *2 Nút Pixel*, *Chạm đi*, *Kết hợp*, tối ưu riêng cho **iPad 4:3**). Sửa triệt để lỗi joystick nhảy xuống rìa màn hình (`UIScale`), nút bánh răng **GMCM** ngoài màn hình Tiêu đề, bàn phím native đặt tên khi tạo nhân vật trên iOS/iPadOS 26–27+, vuốt ve vật nuôi/thú cưng hiện tim ngay trong 1 chạm, thông báo nhặt đồ (`HUDMessage`) ở góc trên-trái khi bật thanh công cụ dọc (Zero-Harmony), và thêm tùy chọn **Tiết kiệm pin 45 FPS** (bên cạnh 60 FPS và 30 FPS, giữ nguyên 60Hz logic game).
- **Mod Garden 9 Tab: Console Log Realtime, Nút Khởi động lại & Chẩn đoán Xung đột Mod** — Chạm **3 ngón tay × 2** để mở Mod Garden (gồm tab mới **Console Log** đọc trực tiếp `SMAPI-latest.txt` / `engine-latest.log` / `launcher-boot.log` theo thời gian thực với bộ lọc màu + sao chép, nút **Khởi động lại** game ngay trên thanh bên, cài `.ZIP/.RAR/.7Z/.TAR.GZ/.XNB`, tự ưu tiên thư mục `Portraits/` cho gói chân dung XNB, tự phát hiện xung đột mod **Chân dung/Anime** như `Seasonal Cute Characters` đè `OhoDavi` & cảnh báo `Portraiture`/`HD Portraits` cần Harmony). Toàn bộ tệp hệ thống tự động ẩn gọn vào `.game/` và `.garden/` — trong **ứng dụng Tệp (Files)** chỉ còn đúng 3 thư mục sạch sẽ: **`Mods`**, **`Saves`**, **`ErrorLogs`**.
- **Tab Báo lỗi Online kèm Ảnh/Video & Log tự động** — Chọn tối đa 3 ảnh chụp màn hình hoặc video (`PHPickerViewController` xoay ngang an toàn, tự nén video MP4 & tự đóng gói `ErrorLogs.zip` đã che thông tin nhạy cảm, gồm cả `native-crash.log`), gửi trực tiếp tới Admin qua Cloudflare Worker (`BugReportWorker/worker.js`).
- **Tương thích trọn vẹn Mod UI, Shader & Tiếng Việt** — Cài đặt đầy đủ `Texture2D.CopyFromTexture` trên iOS MonoGame + fallback ngôn ngữ Tiếng Việt (`*.en.* → *.vi.*` bằng APFS CoW clone 0 byte) + hỗ trợ Shader MGFX v8–v10 GLSL ES (`SDV-Radiance`, `DynamicShader`, `DynamicReflections`...) với FBO-safe `PlatformGetData`, `posFixup` RT sync & GLES 3.0 NPOT.
- **Tự phục hồi SMAPI iOS `4.5.2` · Save 20 mốc · Tối ưu RAM 4 lớp · Co-op LAN / IP** — Tự phục hồi SMAPI iOS khi gói game kẹp bản Windows, nhập/xuất Save đa nền tảng PC/Android/iOS, cảnh báo RAM `85%/90%`, tự xả cache bản đồ/chân dung an toàn GC không gây ô đen, và hỗ trợ Co-op qua Wi-Fi LAN / ZeroTier / Tailscale.

---

## 🚀 Cài đặt nhanh

1. **Cài IPA** bằng KSign / ESign / TrollStore / SideStore / Sideloadly *(cài đè cùng Bundle ID giữ nguyên Save & Mod)*.
2. **Nạp game PC `1.6.15`** *(chỉ 1 lần)*: Nén thư mục game PC (`.ZIP`/`.RAR`/`.7Z`) → mở app → bấm **Nhập gói dữ liệu game** → chờ đủ 4 mục xanh → vuốt tắt và mở lại app.
3. **Cài Mod**: Chạm **3 ngón tay × 2** → tab **Cài Mod** → chọn tệp hoặc thư mục mod → mở lại game để SMAPI nạp mod mới.
4. **Thanh tiện ích trong game**: `BAG` (Túi) · `MAP` (Bản đồ) · `QST` (Nhiệm vụ) · `SAVE` (20 mốc lưu) · `KEY` (Bàn phím) · `HIDE` (Ẩn HUD) · `EDIT` (Chỉnh nút & `+PHIM`).

---

<details>
<summary><b>🛠️ Build từ mã nguồn (Dành cho lập trình viên)</b></summary>

Yêu cầu: base IPA từ [SDViosport](https://github.com/DeadAKJ/SDViosport), `zig cc`, `ld64.lld`, `iPhoneOS.sdk`, `.NET 6.0+ SDK`, `zsign`, `Mono.Cecil.dll`, Python 3.10+.

```powershell
# 1. Build mod điều khiển C# SDVMobileControls
dotnet build ModGarden\SDVMobileControls\SDVMobileControls.csproj -c Release

# 2. Vá IL (MonoGame.Framework.dll, SDViOS.dll, SDViOSTouchControls.dll)
powershell -ExecutionPolicy Bypass -File scripts\patch_perf_v7.ps1

# 3. Đóng gói 2 bản IPA sạch (iOS / iPadOS 26–27+ & iOS / iPadOS 14–18)
python scripts\package_embedded.py base-sdvios.ipa build\SDVModGarden_ios26 zsign.exe dist\Garden-Launcher-1.0.1-iOS26-27.ipa "$WorkDir\v7-host.dll" "$WorkDir\v7-touch.dll" "$WorkDir\Lidgren.Network.dll" --launcher --min-ios=16.0
python scripts\package_embedded.py base-sdvios.ipa build\SDVModGarden_ios14 zsign.exe dist\Garden-Launcher-1.0.1-iOS14-18.ipa "$WorkDir\v7-host.dll" "$WorkDir\v7-touch.dll" "$WorkDir\Lidgren.Network.dll" --launcher --no-scene --min-ios=14.0
```

</details>

---

<p align="center">
  <b>Credits:</b>
  <a href="https://www.stardewvalley.net/">ConcernedApe</a> ·
  <a href="https://github.com/DeadAKJ/SDViosport">SDViosport</a> (MIT) ·
  <a href="https://smapi.io/">SMAPI</a> (LGPL-3.0) ·
  <a href="https://github.com/richgel999/miniz">miniz</a> (MIT)
</p>
