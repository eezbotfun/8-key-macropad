# Host guide: USB CDC customised display protocol (`cus`)

This document describes how a host PC (or other USB host) sends framed messages over the device’s **USB CDC ACM** interface to drive the **customised** app mode on the device. The firmware parses these frames in `[main/config.c](../main/config.c)` and renders them via LVGL in `[main/customised_app.c](../main/customised_app.c)`.

For reference, the device may share the CDC port with other framed protocols (for example keyboard configuration with magic `ebf`, and PC status with `pcs`). Host tools should send **complete `cus` frames** as defined below.

For **now playing** metadata and album art (`pcs` cmd `1231`, USB `player/cover.bin`), see the normative spec in the music player plugin repo: `docs/MEDIA_NOW_PLAYING_DEVICE_PROTOCOL.txt`. Binary layout reference: `docs/cover_bin_format.md` and example `docs/cover.bin`.

---

## 0. Two draw modes

The customised screen has **two mutually exclusive draw modes**. Hosts pick one via `layout` (default = absolute when omitted).

| Mode | `layout` | Like | How you draw | How you refresh |
| ---- | -------- | ---- | ------------ | --------------- |
| **Grid** | `"grid"` | WPF/XAML `Grid` | Setup defines rows/cols and numbered widgets once | Later `set` updates **individual** widgets by `id` — **no** full-screen clear |
| **Absolute** | omit or `"canvas"` | Absolute positioning | Each message places text/image at `x/y/w/h` (stacked overlays) | To redraw cleanly you must **`clear_canvas: true`** (wipe all overlays) then send new panels — **one-shot full refresh**, not per-widget update |

- Grid is for **persistent UI** that changes content over time without rebuilding the whole screen.
- Absolute/canvas is for **simple or ad-hoc** drawing; there are **no stable widget ids**.
- A grid setup replaces canvas overlays. Absolute frames ignore grid `widgets`/`set` when `layout` is not `grid`.

### 0.1 Display size (drawable canvas)

On the **reference 480×320** landscape panel:

| Mode | Drawable canvas | Key strip (on-screen UI) |
| ---- | --------------- | ------------------------ |
| **Strip** (`fullscreen` false / omitted) | **480×200** | Visible **480×120**; not drawable via `cus` |
| **Full-screen** (`fullscreen` true) | **480×320** | Hidden |

**Absolute mode** geometry (`x`, `y`, `w`, `h`) is in pixels with origin at the top-left of the drawable canvas. Defaults: `w = LV_HOR_RES`; `h` = canvas height for current fullscreen mode.

---

## 1. Wire format

Each logical message is one binary frame:

| Offset | Size | Description |
| ------ | ---- | ----------- |
| 0 | 3 bytes | ASCII magic: `c` `u` `s` (0x63, 0x75, 0x73) |
| 3 | 2 bytes | Payload length **N**, **big-endian** uint16 |
| 5 | **N** bytes | UTF-8 JSON object (see §2 / §3) |

- **N** must satisfy **1 ≤ N ≤ 2048** (`PARSER_MAX_DATA_LEN`).
- Practical max: **N ≤ 2043** (`MAX_PRO_BUF_LEN` − 5).

### 1.1 Framing caveats

- Prefer sending the **entire frame in one `write()`**.
- Do not embed **NUL** (0x00) inside the JSON (firmware NUL-terminates for `cJSON_Parse`).
- Keep traffic aligned to intended frame types (`cus` / `pcs` / `ebf`).

---

## 2. Common fields

| Field | Type | Description |
| ----- | ---- | ----------- |
| `layout` | string | `"grid"` or omit/`"canvas"` (absolute). |
| `fullscreen` | bool/int | `true` → full-screen canvas. Default `false`. |
| `cmd` | string | `start` / `stop` / `update`. |
| `activate` | bool/int | **Legacy** when `cmd` omitted: true→`start`, false→`update`. |
| `leds` | object | Per-key RGB. See §5. |

**Font:** embedded `regular` with Montserrat fallback for LVGL symbols. No host-selectable font files.

### 2.1 `align` / `long_mode` / border (text panels)

| `align` | LEFT / CENTER / RIGHT / AUTO |
| `long_mode` | WRAP / SCROLL / SCROLL_CIRCULAR / CLIP / DOT |

`border`, `border-color`, `border-radius` style text panel rectangles (not images).

### 2.2 `cmd` / `activate`

| `cmd` | Behaviour |
| ----- | --------- |
| `start` | Mark customised plugin active; switch to customised app (unless now-playing). |
| `stop` | Exit customised; restore NVS LED config; deactivate plugin flag. |
| `update` | Apply UI/LED updates without forcing app switch. |

When `cmd` omitted: `activate` true → `start`; false → `update`.

### 2.3 LVGL symbols in `text`

Embed named tokens in any `text` string. Firmware expands them to `LV_SYMBOL_*` UTF-8.

Syntax: `{name}` (lowercase), e.g. `{play} Playing`, `{wifi} On {battery_full}`.

Supported names (subset): `ok`, `close`, `play`, `pause`, `stop`, `audio`, `mute`, `volume_mid`, `volume_max`, `wifi`, `battery_empty`, `battery_1`…`battery_3`, `battery_full`, `battery_charge`, `settings`, `home`, `download`, `upload`, `refresh`, `left`, `right`, `up`, `down`, `warning`, `dummy`, `file`, `directory`, `edit`, `save`, `trash`, `power`, `image`, `keyboard`, `list`, `gps`, `call`, `charge`, `eye_open`, `eye_close`, `bluetooth`.

Unknown `{...}` tokens are left unchanged. Avoid unmatched braces in copy.

---

## 3. Grid mode (`layout":"grid"`)

### 3.1 Setup

Send `grid` + `widgets` (rebuilds the grid UI). Caps: **12** widgets, **8** rows, **8** cols. Single JSON frame ≤2048 bytes.

```json
{
  "cmd": "start",
  "layout": "grid",
  "fullscreen": true,
  "grid": {
    "cols": ["*", "*", "*", "*"],
    "rows": ["40", "*", "40"],
    "gap": 4,
    "pad": 6
  },
  "widgets": [
    { "id": 0, "type": "text", "row": 0, "col": 0, "align": "CENTER", "text": "A1" },
    { "id": 1, "type": "text", "row": 0, "col": 1, "align": "CENTER", "text": "A2" },
    { "id": 2, "type": "text", "row": 0, "col": 2, "align": "CENTER", "text": "A3" },
    { "id": 3, "type": "text", "row": 0, "col": 3, "align": "CENTER", "text": "A4" },
    { "id": 4, "type": "text", "row": 1, "col": 0, "col_span": 4,
      "align": "LEFT", "long_mode": "SCROLL_CIRCULAR",
      "text": "Rolling status line — updates can replace or append" },
    { "id": 5, "type": "text", "row": 2, "col": 0, "align": "CENTER", "text": "B1" },
    { "id": 6, "type": "text", "row": 2, "col": 1, "align": "CENTER", "text": "B2" },
    { "id": 7, "type": "text", "row": 2, "col": 2, "align": "CENTER", "text": "B3" },
    { "id": 8, "type": "text", "row": 2, "col": 3, "align": "CENTER", "text": "B4" }
  ]
}
```

Track sizing (WPF-like): `"120"` = pixels; `"*"` = 1 star; `"2*"` = 2 stars.

Widget: `id` (0-based), `type` (`text`|`image`), `row`, `col`, optional `row_span`/`col_span` (default 1), plus `text`/`image`/`fg`/`bg`/`align`/`long_mode`/`border*`.

### 3.2 Update (`set`)

```json
{
  "cmd": "update",
  "layout": "grid",
  "set": [
    { "id": 0, "text": "A1*" },
    { "id": 4, "text": "New ticker", "text_op": "replace" },
    { "id": 4, "text": "line 2", "text_op": "append" }
  ]
}
```

| `text_op` | Behavior (grid text only) |
| --------- | ------------------------- |
| `replace` / omit | Set label text |
| `append` | Append with `\n`; trim from start if UTF-8 length > 512 |

Marquee: `long_mode: SCROLL_CIRCULAR` + usually `text_op: replace`. Multi-line log: `append` + `WRAP`.

Unknown `id` → ignore. Absolute fields (`clear_canvas`, `x/y/w/h` create) ignored in grid mode.

---

## 4. Absolute / canvas mode

| Field | Description |
| ----- | ----------- |
| `x`,`y`,`w`,`h` | Panel rectangle |
| `text` | New stacked text panel (non-empty) |
| `image` | PNG basename under MSC `customised/` |
| `clear_canvas` | Remove all stacked overlays |
| `fg`,`bg`,`align`,`long_mode`,`border*` | Text panel style |

- Cap: **20** overlays; oldest deleted first.
- No stable ids; refresh = `clear_canvas` then redraw.
- `text_op` is **not** used in absolute mode.

### 4.1 Images (MSC)

1. Host writes PNG to `{USB_ROOT}/customised/<name>.png`.
2. Host sends `"image":"<name>.png"` (basename only; `.png`; no path/`..`).
3. Firmware loads `S:/spiflash/customised/<name>.png`.

Do **not** put image bytes or base64 inside `cus` JSON. Desktop bridges may accept `image_b64` over EZBF IPC, write MSC, then strip before CDC.

---

## 5. `leds` (per-key RGB backlight)

Controls the **8× SK6812** key lights (not the LCD panel backlight).

| Field | Description |
| ----- | ----------- |
| `on` | Master enable. `false` → all off. Default when `leds` present: `true`. |
| `keys` | Array length 1..8. Index 0..7. `#RRGGBB`; `null` = unchanged; `"#000000"`/`false` = off. |

On `"cmd":"stop"`, firmware restores NVS LED config (`ws2812_update()`).

---

## 6. Examples

Absolute strip text:

```json
{"x":0,"y":0,"w":480,"h":200,"text":"{ok} Loading…","fg":"#EEEEEE","bg":"#111111","align":"CENTER","cmd":"start"}
```

Grid update append:

```json
{"cmd":"update","layout":"grid","set":[{"id":4,"text":"{wifi} event","text_op":"append"}]}
```

Clear absolute overlays:

```json
{"clear_canvas":true,"cmd":"update"}
```

---

## 7. Limits checklist

| Limit | Value |
| ----- | ----- |
| Strip / full-screen canvas | 480×200 / 480×320 |
| Max stacked absolute overlays | 20 |
| Max grid widgets / rows / cols | 12 / 8 / 8 |
| Append text buffer | 512 UTF-8 bytes |
| Max payload **N** | 2048 |

---

## 8. Related protocols / verification

- **`ebf`**: keyboard / configuration. **`pcs`**: PC status / now playing.
- Verify: `cmd:start` → fullscreen → grid setup → `set` by id → symbols → `leds` → `cmd:stop`.
- Logs: tag `cus_app`.
