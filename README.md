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

- **Quản lý Mod & Việt hóa trong game** — Chạm **3 ngón tay × 2** để mở Mod Garden; hỗ trợ cài trực tiếp `.ZIP`, `.RAR`, `.7Z`, `.TAR.GZ`, `.XNB` hoặc thư mục, bật/tắt, xóa và hoàn tác an toàn.
- **Điều khiển kiểu Mobile & Phím PC** — 4 chế độ (*Vô hình*, *2 Nút*, *Chạm đi A\**, *Kết hợp*), kéo thả chỉnh cỡ nút (`EDIT`), thêm tới 16 phím PC, nút `ESC` đa năng cho mọi menu Mod, chỉnh trực tiếp **Cỡ giao diện UI (`75%–150%`)**, vuốt/đổi hàng Toolbar (`ROW`), nút thả vật phẩm (`DROP`), gắn mồi/phao kéo thả, xoay nội thất (`ROT`), chạm trực tiếp ô xung quanh, đặt nhanh Lò Rèn/Máy May, kéo ngắm súng cao su, lia camera ở Robin/Bảo tàng, chế tạo & bó Junimo 2 chạm an toàn, tự bật bàn phím iOS, tự hiện nút Mini-game và giữ mờ thanh Home Bar (tránh bật sáng khi chạm cạnh dưới).
- **Save Menu 20 mốc + Auto-Save tùy chọn** — Lưu và tải lại tới 20 bản lưu nhanh cùng tùy chọn tự lưu định kỳ slot `[AUTO]` khi đứng yên ngoài hang/mỏ.
- **Hỗ trợ Shader & Âm thanh chuẩn iOS** — Tương thích mod Shader (`DynamicReflections`, `DynamicShader`...) kèm phản chiếu nhân vật trên mặt nước và âm thanh OpenAL ổn định.
- **Cảnh báo RAM tự động (`85%` / `90%`) & Tối ưu Cache Map/Portrait/Thời tiết** — Tự động xả cache bản đồ cũ, chân dung HD NPC ngoài màn hình & bản sao trùng của Content Patcher khi chuyển khu vực (không nén ảnh, không ảnh hưởng âm thanh), kèm giảm hạt lá/cánh hoa bay giữ mượt FPS và cảnh báo nhắc lưu game kịp thời.
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
