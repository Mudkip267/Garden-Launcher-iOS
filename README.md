<div align="center">
  <img src="assets/Garden-Launcher-Icon.png" alt="Garden Launcher" width="104" />
  <h1>Garden Launcher</h1>
  <p><b>Launcher &amp; Mod Manager cho Stardew Valley PC 1.6.15 trên iOS</b></p>

  <p>
    <a href="https://github.com/hanprm9-dot/Garden-Launcher-iOS/releases/tag/v7.10"><img src="https://img.shields.io/badge/Version-v7.10-2ea44f?style=flat-square" alt="Version v7.10" /></a>
    <img src="https://img.shields.io/badge/iOS-14%20–%2027+-007aff?style=flat-square&logo=apple&logoColor=white" alt="iOS 14-27+" />
    <img src="https://img.shields.io/badge/SMAPI-4.5.2-6f42c1?style=flat-square" alt="SMAPI 4.5.2" />
    <img src="https://img.shields.io/badge/JIT-Không%20cần%20(0%25)-f59e0b?style=flat-square" alt="0% JIT" />
    <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-64748b?style=flat-square" alt="MIT License" /></a>
  </p>
</div>

> [!NOTE]
> **Launcher sạch (0% tệp bản quyền):** Kho mã nguồn và các bản `.ipa` không chứa dữ liệu game của ConcernedApe. Bạn cần tự chuẩn bị bộ dữ liệu **Stardew Valley PC 1.6.15** của riêng mình.

---

## 📥 Tải xuống

| Thiết bị | Phiên bản IPA | Tải về |
| :--- | :--- | :---: |
| **iOS 14 – iOS 18** | `Garden-Launcher-v7.10-iOS14-18.ipa` | [**⬇️ Tải ngay**](https://github.com/hanprm9-dot/Garden-Launcher-iOS/releases/download/v7.10/Garden-Launcher-v7.10-iOS14-18.ipa) |
| **iOS 26 – iOS 27+** | `Garden-Launcher-v7.10-iOS26-27.ipa` | [**⬇️ Tải ngay**](https://github.com/hanprm9-dot/Garden-Launcher-iOS/releases/download/v7.10/Garden-Launcher-v7.10-iOS26-27.ipa) |

---

## ✨ Tính năng chính

- **Quản lý Mod & Chẩn đoán Xung đột trong game** — Chạm **3 ngón tay × 2** để mở Mod Garden (giao diện gốc Apple native); hỗ trợ cài trực tiếp `.ZIP`, `.RAR`, `.7Z`, `.TAR.GZ`, `.XNB` hoặc thư mục (tự giải mã đúng tên thư mục tiếng Trung/Nhật/Hàn), bật/tắt, xóa và hoàn tác an toàn kể cả sau khi đã xóa mod. Tự động **cảnh báo mod dành riêng cho bản Mobile/Android** (hiện khung cảnh báo + hộp thoại xác nhận trước khi cài) và **Chẩn đoán Mod** ngay trong Thư viện (xung đột Content Patcher `Action: Load`, thiếu/tắt mod phụ thuộc, cài trùng `UniqueID`, mod C# đời cũ 1.5, thư mục lỗi thiếu `manifest.json` kèm nút `Dọn` 1 chạm, và đọc lỗi `Skipped mods`/`ERROR` từ `SMAPI-latest.txt`).
- **Màn hình Loading khởi động Native** — Thay thế màn hình đen khi SMAPI & Content Patcher đang nạp mod bằng màn hình chờ gốc Apple (logo Garden Launcher, vòng xoay CoreAnimation GPU 60 FPS không giật, tiến độ nạp thời gian thực), tự mờ dần và giải phóng `100%` RAM ngay khi màn hình Tiêu đề vẽ khung hình đầu tiên (vẫn hỗ trợ chạm 3 ngón × 2 mở Mod Garden ngay lúc đang tải).
- **Tương thích trọn vẹn Mod UI & Tiếng Việt (`Content Patcher`)** — Bổ sung hoàn chỉnh `Texture2D.CopyFromTexture` trên iOS MonoGame để mọi gói giao diện/retexture/recolor/chân dung (`Cat Valley UI`, `Earthy Interface`, `Vintage Interface`...) cập nhật trực tiếp ngay từ màn hình Tiêu đề; tự tạo bản dự phòng ngôn ngữ Tiếng Việt (`*.en.* → *.vi.*`) bằng **APFS CoW clone 0 byte**; tự chặn `.xnb` bản Mobile/Android ghi đè làm hỏng `Content/` PC; toàn bộ UI di động tự đồng bộ texture gốc của game (`Game1.menuTexture` / `Game1.mouseCursors` / `ShopMenu.VisualTheme`).
- **Tự phục hồi SMAPI iOS & Nhập/Xuất File Save đa nền tảng** — Tự phát hiện và phục hồi SMAPI iOS `4.5.2` khi gói dữ liệu game kẹp bản SMAPI Windows; hỗ trợ Nhập & Xuất File Save tương thích giữa PC, Android và iOS (tự dò XML 128 KB + sửa lỗi 1 chạm).
- **Điều khiển Pixel-Art kiểu Mobile & Phím PC** — 4 chế độ (*Vô hình*, *2 Nút*, *Chạm đi A\**, *Kết hợp*) kèm tự chọn công cụ/vũ khí 36 ô (`Auto-Tool`/`Auto-Combat`), nút chiêu đặc biệt `SPEC`, biểu cảm `Emote`, **Toolbar dọc cạnh trái 36 ô** (chỉnh trực tiếp Độ dày & Độ trong suốt), **nút `HIDE` ẩn toàn bộ HUD** (chạm đúp để hiện lại), hỗ trợ tay cầm Bluetooth thật, giữ lực câu cá mượt trên mọi chế độ, mở cửa nhà & hòm thư 1 chạm không lặp thông báo, chỉnh `UI Scale` (`75%–150%`), Pinch-Zoom, nút `ESC` đa năng và giữ mờ thanh Home Bar.
- **Save Menu 20 mốc + Auto-Save tùy chọn** — Lưu và tải lại tới 20 bản lưu nhanh cùng tùy chọn tự lưu định kỳ slot `[AUTO]` khi đứng yên ngoài hang/mỏ.
- **Hỗ trợ Shader & Âm thanh chuẩn iOS** — Tương thích mod Shader (`DynamicReflections`, `DynamicShader`, `SDV-Radiance`...) hỗ trợ MGFX v8–v10 GLSL ES và âm thanh OpenAL/AVAudioSession ổn định.
- **Cảnh báo RAM tự động (`85%` / `90%`) & Tối ưu Cache Map/Portrait/Pin** — Tự động xả cache bản đồ cũ, chân dung HD NPC ngoài màn hình & bản sao trùng của Content Patcher khi chuyển khu vực (an toàn GC không gây ô đen), giảm hạt thời tiết thừa và tùy chọn **Tiết kiệm pin 30 FPS** *(giữ nguyên tốc độ logic 60Hz)*.
- **Co-op LAN / IP** — Hỗ trợ chơi cùng Wi-Fi LAN hoặc qua ZeroTier / Tailscale.

---

## 🚀 Cài đặt nhanh

1. **Cài IPA** bằng KSign / ESign / TrollStore / SideStore / Sideloadly *(cài đè cùng Bundle ID giữ nguyên Save & Mod)*.
2. **Nạp game PC `1.6.15`** *(chỉ 1 lần)*: Nén thư mục game PC (`.ZIP`/`.RAR`/`.7Z`) → mở app → bấm **Nhập gói dữ liệu game** → chờ đủ 4 mục xanh → vuốt tắt và mở lại app.
3. **Cài Mod**: Chạm **3 ngón tay × 2** → tab **Cài Mod** → chọn tệp hoặc thư mục mod → mở lại game để SMAPI nạp mod mới.
4. **Thanh tiện ích trong game**: `BAG` (Túi) · `MAP` (Bản đồ) · `QST` (Nhiệm vụ) · `SAVE` (20 mốc lưu) · `KEY` (Bàn phím) · `EDIT` (Chỉnh nút & `+PHIM`).

---

<details>
<summary><b>🛠️ Build từ mã nguồn (Dành cho lập trình viên)</b></summary>

Yêu cầu: base IPA từ [SDViosport](https://github.com/DeadAKJ/SDViosport), `zig cc`, `ld64.lld`, `iPhoneOS.sdk`, `.NET 6.0+ SDK`, `zsign`, `Mono.Cecil.dll`, Python 3.10+.

```powershell
# 1. Build mod điều khiển C# SDVMobileControls
dotnet build ModGarden\SDVMobileControls\SDVMobileControls.csproj -c Release

# 2. Vá IL (MonoGame.Framework.dll, SDViOS.dll, SDViOSTouchControls.dll)
powershell -ExecutionPolicy Bypass -File scripts\patch_perf_v7.ps1

# 3. Đóng gói 2 bản IPA sạch (iOS 26–27+ & iOS 14–18)
python scripts\package_embedded.py base-sdvios.ipa build\SDVModGarden_ios26 zsign.exe dist\Garden-Launcher-v7.10-iOS26-27.ipa "$WorkDir\v7-host.dll" "$WorkDir\v7-touch.dll" "$WorkDir\Lidgren.Network.dll" --launcher --min-ios=16.0
python scripts\package_embedded.py base-sdvios.ipa build\SDVModGarden_ios14 zsign.exe dist\Garden-Launcher-v7.10-iOS14-18.ipa "$WorkDir\v7-host.dll" "$WorkDir\v7-touch.dll" "$WorkDir\Lidgren.Network.dll" --launcher --no-scene --min-ios=14.0
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
