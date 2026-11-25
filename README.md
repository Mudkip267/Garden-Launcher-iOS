<div align="center">
  <img src="assets/Garden-Launcher-Icon.png" alt="Garden Launcher" width="108" />
  <h1>Garden Launcher</h1>
  <p><b>Launcher &amp; Mod Manager cho Stardew Valley PC 1.6.15 trên iOS &amp; iPadOS</b></p>

  <p>
    <a href="https://github.com/hanprm9-dot/Garden-Launcher-iOS/releases/tag/v1.0.3"><img src="https://img.shields.io/badge/Version-v1.0.3-2ea44f?style=flat-square" alt="Version v1.0.3" /></a>
    <img src="https://img.shields.io/badge/iOS%20%2F%20iPadOS-14%20–%2027+-007aff?style=flat-square&logo=apple&logoColor=white" alt="iOS & iPadOS 14-27+" />
    <img src="https://img.shields.io/badge/SMAPI-4.5.2-6f42c1?style=flat-square" alt="SMAPI 4.5.2" />
    <img src="https://img.shields.io/badge/JIT-Không%20cần%20(0%25)-f59e0b?style=flat-square" alt="0% JIT" />
    <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-64748b?style=flat-square" alt="MIT License" /></a>
  </p>
</div>

> [!NOTE]
> **Launcher sạch (0% tệp bản quyền):** Kho mã nguồn và các bản `.ipa` không chứa dữ liệu game của ConcernedApe. Bạn cần tự chuẩn bị bộ dữ liệu **Stardew Valley PC 1.6.15** của riêng mình.

---

## 📥 Tải xuống (`v1.0.3`)

| Thiết bị | Phiên bản IPA | Tải về |
| :--- | :--- | :---: |
| **iOS / iPadOS 14 – 18** | `Garden-Launcher-1.0.3-iOS14-18.ipa` | [**⬇️ Tải ngay**](https://github.com/hanprm9-dot/Garden-Launcher-iOS/releases/download/v1.0.3/Garden-Launcher-1.0.3-iOS14-18.ipa) |
| **iOS / iPadOS 26 – 27+** | `Garden-Launcher-1.0.3-iOS26-27.ipa` | [**⬇️ Tải ngay**](https://github.com/hanprm9-dot/Garden-Launcher-iOS/releases/download/v1.0.3/Garden-Launcher-1.0.3-iOS26-27.ipa) |

---

## ✨ Điểm mới nổi bật trong `v1.0.3`

- **Logo Slime Khung Đá Rêu Pixel 16-bit & Mở Quyền Truy Cập `StardewValley/Content` trong Tệp** — Thay bộ biểu tượng ứng dụng Slime xanh lá trong khung đá rêu cổ điển; khôi phục thư mục hiển thị **`Tệp → Trên iPhone → Garden Launcher → StardewValley/Content`** (tự chuyển `.game/` cũ sang `StardewValley/` tức thì) và bổ sung thẻ **Nạp lại Game / Content (`.ZIP/.RAR/.7Z` hoặc Thư mục)** ngay trong tab **Cài Mod** kèm tự động chuyển `libarchive` cho gói nén Deflate64 Windows (`#GL-FKRBMM`).
- **Đại Tu Toàn Diện Tay Cầm Console & Rung `CoreHaptics`** — Đồng bộ trực tiếp `SInputState` + `SnappyMenus` cùng khung hình trên nền `0% JIT`: tự bám ô đầu tiên ngay khi mở Menu, chuyển tab & nhảy qua lại giữa các khung **Balô ↔ Rương ↔ Trang bị/Thùng rác ↔ Hàng hóa Shop** bằng **`LB` / `RB` / `LT` / `RT`**, hỗ trợ đủ **Thay đồ (Swap)**, **Vứt đồ ra ngoài (Drop)** (sửa kẹp biên màn hình iPhone với vùng thả `-99998`), **Giữ `A` + Di chuyển + Nhả `A` để Kéo-Thả (Drag & Drop)** song song với nhấn `A` 2 lần kiểu Console, lái con trỏ tự do bằng **R-Stick** trong mọi Menu (kể cả thanh trượt GMCM & icon **UI Info Suite 2 v2.3.7**) và rung phản hồi **`CoreHaptics`** 2 mô-tơ (tích hợp cùng đóng góp của `@Mudkip267`).
- **Chế Độ Tay Cầm Thuần (`Pure Gamepad Mode`) kèm Khởi Động Lại 1 Chạm** — Công tắc mới ngay đầu tab **Điều khiển**: tắt hoàn toàn HUD ảo & lớp chặn cảm ứng ngay từ lúc khởi động để chơi thuần như máy Console cầm tay (Backbone / Razer Kishi / Xbox / DualSense / Switch Pro) mà **không xung đột** với các bản vá tay cầm hay bàn phím đặt tên iOS.
- **Sửa Dứt Điểm Lỗi Không Di Chuyển Được Trong Event & Lễ Hội** — Mở khóa hoàn toàn di chuyển, tìm đường A* và tương tác nói chuyện / quầy hàng / câu cá trong mọi sự kiện & lễ hội có điều khiển nhân vật (`Event.playerControlSequence`, vd: Lễ hội Trứng, Vũ điệu Hoa, Hội chợ, Đêm Linh Hồn...), đồng thời hiển thị nút **SKIP** chuẩn sprite gốc cho các đoạn cắt cảnh thường (tự nhận giao diện Cat Valley UI).
- **Đồng Bộ Chân Dung Anime Theo Mùa, Tương Thích `UI Info Suite 2` & Tối Ưu Hầm Mỏ** — Tự động tải lại chân dung NPC khi Content Patcher đổi mùa/trang phục (`AssetsInvalidated` không gây mất ảnh hội thoại), bổ sung `CueDefinition` trong `MonoGame.Framework.dll` cho **UI Info Suite 2 v2.3.7+**, dời số tầng Hầm mỏ / Hang Sọ / Núi lửa thoát khỏi góc bo tròn iPhone, và đưa bộ dọn RAM 2 mức (`85%` nhẹ / `91%` khẩn cấp) sang hàng đợi nền chống khựng (đóng góp bởi `@Mudkip267`).

---

## 🌟 Hệ thống tính năng cốt lõi

- **Màn hình Loading Pixel Art 16-bit chuẩn `0% → 100%` & Tự dọn Crash Marker** — Thanh tiến độ RPG có Junimo pixel nhảy theo mép thanh, 5 mốc giai đoạn (`.NET · SMAPI · Mods · Nội dung · Vào Game`), bảng RAM live và tự dọn `StardewModdingAPI.crash.marker` chống kẹt màn hình tải.
- **4 Chế Độ Cảm Ứng Mobile + Ngoại Vi Đầy Đủ** — *Vô hình*, *2 Nút Pixel*, *Chạm đi A\**, *Kết hợp* (tối ưu riêng cho iPad 4:3), kéo cuộn Shop 1:1 + thanh trượt `1..999`, bàn phím vật lý (`WASD`, `F1–F12`, phím tắt mod SMAPI), chuột/trackpad PC và tiết kiệm pin `60 / 45 / 30 FPS`.
- **Mod Garden 9 Tab & Kiểm Tra Cập Nhật Tự Động** — Chạm **3 ngón tay × 2** để mở Mod Garden ngay trong game: cài Mod & `.XNB`, **Console Log** thời gian thực, 20 mốc **Lưu nhanh**, **Báo lỗi Online** kèm ảnh/video & `ErrorLogs.zip`, chẩn đoán xung đột mod và tự kiểm tra phiên bản mới qua `version.json`.
- **Tương Thích Mod UI, Shader MGFX v8–v10, Tiếng Việt & Co-op LAN / IP** — Hỗ trợ `Texture2D.CopyFromTexture`, GLSL ES `highp` (`DynamicShader`, `DynamicReflections`, `SDV-Radiance`), tự phục hồi SMAPI iOS `4.5.2`, bảo vệ hòm thư `Data/mail` 1.6 và chơi chung qua Wi-Fi LAN / ZeroTier / Tailscale.

---

## 🚀 Cài đặt nhanh

1. **Cài IPA** bằng KSign / ESign / TrollStore / SideStore / Sideloadly *(cài đè cùng Bundle ID giữ nguyên Save & Mod)*.
2. **Nạp game PC `1.6.15`**: Nén thư mục game PC (`.ZIP`/`.RAR`/`.7Z`) → mở app → bấm **Nhập gói dữ liệu game** *(hoặc chép thẳng vào `Tệp → Trên iPhone → Garden Launcher → StardewValley`)* → chờ đủ 4 mục xanh → mở lại app.
3. **Cài Mod / Thay `.XNB`**: Chạm **3 ngón tay × 2** → tab **Cài Mod** → chọn gói Mod/XNB (hoặc sửa trực tiếp trong `StardewValley/Content` rồi nạp lại) → bấm **Khởi động lại**.
4. **Dùng Tay cầm**: Cắm/kết nối tay cầm để game tự nhận ngay, hoặc vào **Mod Garden → Điều khiển → Chế độ Tay cầm thuần** rồi bấm **Khởi động lại ngay** nếu chỉ chơi bằng tay cầm.

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
python scripts\package_embedded.py base-sdvios.ipa build\SDVModGarden_ios26 zsign.exe dist\Garden-Launcher-1.0.3-iOS26-27.ipa "$WorkDir\v7-host.dll" "$WorkDir\v7-touch.dll" "$WorkDir\Lidgren.Network.dll" --launcher --min-ios=16.0
python scripts\package_embedded.py base-sdvios.ipa build\SDVModGarden_ios14 zsign.exe dist\Garden-Launcher-1.0.3-iOS14-18.ipa "$WorkDir\v7-host.dll" "$WorkDir\v7-touch.dll" "$WorkDir\Lidgren.Network.dll" --launcher --no-scene --min-ios=14.0
```

</details>

---

<p align="center">
  <b>Credits:</b>
  <a href="https://www.stardewvalley.net/">ConcernedApe</a> ·
  <a href="https://github.com/DeadAKJ/SDViosport">SDViosport</a> (MIT) ·
  <a href="https://smapi.io/">SMAPI</a> (LGPL-3.0) ·
  <a href="https://github.com/Mudkip267/Garden-Launcher-iOS">Mudkip267 (Nguyễn Ngọc Nguyên)</a> ·
  <a href="https://github.com/richgel999/miniz">miniz</a> (MIT)
</p>
