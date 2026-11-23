// Garden Launcher iOS — Bug Report Bridge (Cloudflare Worker -> Discord Webhook)
// Supports sanitized ErrorLogs.zip + up to 3 image/video attachments (JPEG/PNG/WEBP/GIF/MP4/MOV)!
// Required Secret in Worker Settings -> Variables & Secrets:
//   DISCORD_WEBHOOK_URL = https://discord.com/api/webhooks/YOUR_ID/YOUR_TOKEN
// Optional Secret/Var:
//   REPORT_CLIENT_KEY   = your-secret-client-key (must match SDV_BUG_REPORT_CLIENT_KEY at build time)

const DEFAULT_CLIENT_KEY = "CHANGE_ME_CLIENT_KEY";
const MAX_ZIP_BYTES = 4 * 1024 * 1024;    // 4 MB cap for ErrorLogs.zip
const MAX_MEDIA_BYTES = 7.5 * 1024 * 1024;// 7.5 MB per image/video
const MAX_TOTAL_BYTES = 9.2 * 1024 * 1024;// 9.2 MB combined (Discord free webhook limit ~10 MB)
const MIN_COOLDOWN_MS = 45 * 1000;        // 45s minimum between reports from same IP/device
const WINDOW_MS = 15 * 60 * 1000;         // 15-minute rolling window
const MAX_PER_WINDOW = 5;                 // Max 5 reports per 15 min per IP

const rateMap = new Map();

function checkRateLimit(key) {
  const now = Date.now();
  let rec = rateMap.get(key);
  if (!rec || now - rec.winStart > WINDOW_MS) {
    rec = { winStart: now, count: 0, lastAt: 0 };
  }
  if (rec.lastAt && now - rec.lastAt < MIN_COOLDOWN_MS) {
    const waitSec = Math.ceil((MIN_COOLDOWN_MS - (now - rec.lastAt)) / 1000);
    return { ok: false, retryAfter: waitSec, reason: `Vui lòng đợi ${waitSec} giây trước khi gửi báo lỗi tiếp theo.` };
  }
  if (rec.count >= MAX_PER_WINDOW) {
    return { ok: false, retryAfter: 300, reason: "Bạn đã gửi quá nhiều báo lỗi trong 15 phút qua. Vui lòng thử lại sau." };
  }
  rec.count += 1;
  rec.lastAt = now;
  rateMap.set(key, rec);
  if (rateMap.size > 5000) {
    for (const [k, v] of rateMap) {
      if (now - v.winStart > WINDOW_MS) rateMap.delete(k);
    }
  }
  return { ok: true };
}

function clampStr(val, maxLen, fallback = "") {
  const s = (typeof val === "string" ? val : "").trim();
  if (!s) return fallback;
  return s.length > maxLen ? s.slice(0, maxLen - 1) + "…" : s;
}

function categoryColor(cat) {
  const low = (cat || "").toLowerCase();
  if (low.includes("crash") || low.includes("văng")) return 0xe53935;
  if (low.includes("mod") || low.includes("xung")) return 0xfb8c00;
  if (low.includes("điều khiển") || low.includes("cảm ứng")) return 0x1e88e5;
  if (low.includes("đồ họa") || low.includes("shader")) return 0x8e24aa;
  if (low.includes("âm thanh")) return 0x00897b;
  return 0xfbc02d;
}

function makeReportId() {
  const chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  const bytes = new Uint8Array(6);
  crypto.getRandomValues(bytes);
  let out = "GL-";
  for (let i = 0; i < bytes.length; i++) out += chars[bytes[i] % chars.length];
  return out;
}

function detectAllowedMedia(buf, origName) {
  const b = new Uint8Array(buf, 0, Math.min(16, buf.byteLength));
  if (b.length < 8) return null;
  // JPEG: FF D8 FF
  if (b[0] === 0xff && b[1] === 0xd8 && b[2] === 0xff) {
    return { ext: "jpg", mime: "image/jpeg" };
  }
  // PNG: 89 50 4E 47
  if (b[0] === 0x89 && b[1] === 0x50 && b[2] === 0x4e && b[3] === 0x47) {
    return { ext: "png", mime: "image/png" };
  }
  // GIF: GIF8
  if (b[0] === 0x47 && b[1] === 0x49 && b[2] === 0x46 && b[3] === 0x38) {
    return { ext: "gif", mime: "image/gif" };
  }
  // WEBP: RIFF....WEBP
  if (b.length >= 12 && b[0] === 0x52 && b[1] === 0x49 && b[2] === 0x46 && b[3] === 0x46 &&
      b[8] === 0x57 && b[9] === 0x45 && b[10] === 0x42 && b[11] === 0x50) {
    return { ext: "webp", mime: "image/webp" };
  }
  // ISO BMFF Video (MP4 / MOV): bytes 4..7 == "ftyp" (66 74 79 70)
  if (b[4] === 0x66 && b[5] === 0x74 && b[6] === 0x79 && b[7] === 0x70) {
    const low = (origName || "").toLowerCase();
    const isMov = low.endsWith(".mov") || (b.length >= 10 && b[8] === 0x71 && b[9] === 0x74);
    return isMov ? { ext: "mov", mime: "video/quicktime" } : { ext: "mp4", mime: "video/mp4" };
  }
  return null;
}

function jsonResp(obj, status = 200) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
    },
  });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method === "GET") {
      return jsonResp({
        ok: true,
        service: "Garden Launcher Bug Report Bridge",
        version: "1.1",
        mediaSupport: true,
        configured: Boolean(env.DISCORD_WEBHOOK_URL),
        endpoint: `${url.origin}/report`,
      });
    }

    if (request.method !== "POST") {
      return jsonResp({ ok: false, error: "Method not allowed" }, 405);
    }

    if (!env.DISCORD_WEBHOOK_URL) {
      return jsonResp({ ok: false, error: "Máy chủ chưa cấu hình DISCORD_WEBHOOK_URL." }, 503);
    }

    const appHdr = request.headers.get("X-Garden-App") || "";
    const keyHdr = request.headers.get("X-Garden-Key") || "";
    const expectedKey = env.REPORT_CLIENT_KEY || DEFAULT_CLIENT_KEY;
    if (appHdr !== "GardenLauncher-iOS" || keyHdr !== expectedKey) {
      return jsonResp({ ok: false, error: "Yêu cầu không hợp lệ." }, 403);
    }

    const ip = request.headers.get("CF-Connecting-IP") || "unknown";
    let form;
    try {
      form = await request.formData();
    } catch {
      return jsonResp({ ok: false, error: "Dữ liệu gửi lên không đúng định dạng multipart/form-data." }, 400);
    }

    const deviceHash = clampStr(form.get("deviceHash"), 64, "anon");
    const rl = checkRateLimit(`${ip}|${deviceHash}`);
    if (!rl.ok) {
      return jsonResp({ ok: false, error: rl.reason }, 429);
    }

    const category = clampStr(form.get("category"), 80, "Khác");
    const description = clampStr(form.get("description"), 2000, "");
    const contact = clampStr(form.get("contact"), 140, "Không cung cấp");
    const appVersion = clampStr(form.get("appVersion"), 40, "1.0.1");
    const iosVersion = clampStr(form.get("iosVersion"), 40, "?");
    const device = clampStr(form.get("device"), 64, "iPhone");
    const ramInfo = clampStr(form.get("ramInfo"), 120, "?");
    const controlMode = clampStr(form.get("controlMode"), 60, "?");
    const modCount = clampStr(form.get("modCount"), 32, "0");
    const modsSummary = clampStr(form.get("modsSummary"), 950, "Không có");
    const issueSummary = clampStr(form.get("issueSummary"), 900, "");

    if (description.length < 8) {
      return jsonResp({ ok: false, error: "Vui lòng nhập mô tả lỗi ít nhất 8 ký tự." }, 400);
    }

    let totalBytes = 0;

    // 1. Validate optional ErrorLogs.zip attachment
    let zipFile = null;
    const rawFile = form.get("file");
    if (rawFile && typeof rawFile === "object" && typeof rawFile.arrayBuffer === "function" && rawFile.size > 0) {
      if (rawFile.size > MAX_ZIP_BYTES) {
        return jsonResp({ ok: false, error: "Tệp ErrorLogs.zip vượt quá giới hạn 4 MB." }, 413);
      }
      const buf = await rawFile.arrayBuffer();
      const head = new Uint8Array(buf, 0, Math.min(4, buf.byteLength));
      if (head.length < 4 || head[0] !== 0x50 || head[1] !== 0x4b || head[2] !== 0x03 || head[3] !== 0x04) {
        return jsonResp({ ok: false, error: "Tệp đính kèm không phải định dạng .ZIP hợp lệ." }, 400);
      }
      zipFile = buf;
      totalBytes += buf.byteLength;
    }

    // 2. Validate up to 3 optional image/video attachments (media0, media1, media2)
    const mediaList = [];
    for (let i = 0; i < 3; i++) {
      const m = form.get(`media${i}`);
      if (!m || typeof m !== "object" || typeof m.arrayBuffer !== "function" || m.size <= 0) continue;
      if (m.size > MAX_MEDIA_BYTES) {
        return jsonResp({ ok: false, error: "Tệp ảnh/video vượt quá giới hạn 7.5 MB." }, 413);
      }
      const mbuf = await m.arrayBuffer();
      const det = detectAllowedMedia(mbuf, m.name || "");
      if (!det) {
        return jsonResp({ ok: false, error: "Chỉ hỗ trợ đính kèm hình ảnh (JPG/PNG/WEBP/GIF) và video (MP4/MOV)." }, 400);
      }
      if (totalBytes + mbuf.byteLength > MAX_TOTAL_BYTES) {
        return jsonResp({ ok: false, error: "Tổng dung lượng các tệp đính kèm vượt quá 9 MB." }, 413);
      }
      totalBytes += mbuf.byteLength;
      mediaList.push({ buf: mbuf, ext: det.ext, mime: det.mime });
    }

    const reportId = makeReportId();
    const fields = [
      { name: "Loại lỗi", value: `\`${category}\``, inline: true },
      { name: "Phiên bản", value: `\`Garden Launcher v${appVersion}\``, inline: true },
      { name: "Thiết bị", value: `\`${device}\` · iOS \`${iosVersion}\``, inline: true },
      { name: "RAM", value: `\`${ramInfo}\``, inline: true },
      { name: "Điều khiển", value: `\`${controlMode}\``, inline: true },
      { name: "Liên hệ", value: contact, inline: true },
      { name: `Mod đang bật (${modCount})`, value: `\`\`\`\n${modsSummary}\n\`\`\``, inline: false },
    ];
    if (issueSummary) {
      fields.push({ name: "Cảnh báo Mod Garden phát hiện", value: `\`\`\`\n${issueSummary}\n\`\`\``, inline: false });
    }

    const footerParts = [];
    if (zipFile) footerParts.push(`ErrorLogs-${reportId}.zip (${Math.max(1, Math.round(zipFile.byteLength / 1024))} KB)`);
    if (mediaList.length > 0) footerParts.push(`${mediaList.length} ảnh/video đính kèm`);
    if (footerParts.length === 0) footerParts.push("Không có tệp đính kèm");

    const discordForm = new FormData();
    const attachments = [];
    let fileIdx = 0;
    if (zipFile) {
      const zipName = `ErrorLogs-${reportId}.zip`;
      attachments.push({ id: fileIdx, filename: zipName });
      discordForm.append(
        `files[${fileIdx++}]`,
        new Blob([zipFile], { type: "application/zip" }),
        zipName
      );
    }
    for (let i = 0; i < mediaList.length; i++) {
      const m = mediaList[i];
      const mediaName = `media-${reportId}-${i + 1}.${m.ext}`;
      attachments.push({ id: fileIdx, filename: mediaName });
      discordForm.append(
        `files[${fileIdx++}]`,
        new Blob([m.buf], { type: m.mime }),
        mediaName
      );
    }

    const payload = {
      username: "Garden Launcher Bug Bot",
      embeds: [
        {
          title: `🐞 Báo lỗi #${reportId} — ${category}`,
          description,
          color: categoryColor(category),
          fields,
          footer: { text: footerParts.join(" · ") },
          timestamp: new Date().toISOString(),
        },
      ],
      attachments,
    };
    discordForm.append("payload_json", JSON.stringify(payload));

    const webhookUrl = env.DISCORD_WEBHOOK_URL.includes("?")
      ? `${env.DISCORD_WEBHOOK_URL}&wait=true`
      : `${env.DISCORD_WEBHOOK_URL}?wait=true`;

    const dResp = await fetch(webhookUrl, {
      method: "POST",
      body: discordForm,
    });

    if (!dResp.ok) {
      const errTxt = await dResp.text().catch(() => "");
      return jsonResp(
        { ok: false, error: `Discord từ chối gửi báo lỗi (HTTP ${dResp.status}).`, detail: errTxt.slice(0, 200) },
        502
      );
    }

    return jsonResp({ ok: true, reportId, mediaCount: mediaList.length }, 200);
  },
};
