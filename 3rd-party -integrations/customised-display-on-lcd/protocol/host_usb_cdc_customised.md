# Host guide: USB CDC customised display protocol (`cus`)

This document describes how a host PC (or other USB host) sends framed messages over the device’s **USB CDC ACM** interface to drive the **customised** full-screen app mode on the device. The firmware parses these frames in `[main/config.c](../main/config.c)` and renders them via LVGL in `[main/customised_app.c](../main/customised_app.c)`.

For reference, the device may share the CDC port with other framed protocols (for example keyboard configuration with magic `ebf`, and PC status with `pcs`). Host tools should send **complete `cus` frames** as defined below.

---

## 0. Display layout (drawable canvas)

On the **reference 480×320** landscape panel used in this project, the host can draw **only** the top **200 × 480** pixels (read as **200 rows × 480 columns** — i.e. **480 px wide** and **200 px tall**). That rectangle is the entire **drawable canvas** for the `cus` protocol: the bottom **120 × 480** px are reserved for on-device keys and are **not** part of this canvas.

In firmware (see `[main/customised_app.c](../main/customised_app.c)`), the same idea is expressed as:

| Band              | Size (pixels)                         | Role |
| ----------------- | ------------------------------------- | ---- |
| **Drawable canvas** | **`LV_HOR_RES` × (`LV_VER_RES` − 120)** | Only region host JSON can place the panel/label. On 480×320 this is **480×200**. |
| **Key strip**     | **`LV_HOR_RES` × 120**                | Eight shortcut keys; **not** drawable via `cus`. |

### Coordinate system

All JSON geometry (**`x`**, **`y`**, **`w`**, **`h`**) is in **pixels** with the **origin at the top-left corner of the drawable canvas** `(0, 0)`. Increasing **`x`** moves right; increasing **`y`** moves down. The canvas has no other offset — you do **not** use coordinates relative to the full physical display. The firmware clamps `x`, `y`, `w`, `h` so the panel stays inside this canvas (`clamp_rect_custom_area`).

**JSON defaults** when `w` / `h` are omitted: **`w` = `LV_HOR_RES`**, **`h` = `LV_VER_RES − 120`** (the full canvas height on the reference build: **200**).

The panel widget uses **6 px padding** on all sides for the label; the outer `(x, y, w, h)` box is what you set in JSON.

If `LV_VER_RES` is not 320, the canvas height is **`LV_VER_RES − 120`** px; width remains **`LV_HOR_RES`** (often 480).

---

## 1. Wire format

Each logical message is one binary frame:


| Offset | Size        | Description                                                               |
| ------ | ----------- | ------------------------------------------------------------------------- |
| 0      | 3 bytes     | ASCII magic: `**c`** `**u**` `**s**` (0x63, 0x75, 0x73)                   |
| 3      | 2 bytes     | Payload length **N**, **big-endian** unsigned 16-bit (`len_hi`, `len_lo`) |
| 5      | **N** bytes | Payload: UTF-8 encoded JSON object (see §2)                               |


- **N** must satisfy **1 ≤ N ≤ 2048** (current firmware limit: `[PARSER_MAX_DATA_LEN](../main/parser.c)`).
- The JSON payload must not exceed what fits in the device’s internal stream buffer after framing; the reassembly buffer is **2048 bytes** (`[MAX_PRO_BUF_LEN](../main/config.c)`), so in practice keep **5 + N ≤ 2048**, i.e. **N ≤ 2043** for a single frame.

The device accumulates incoming CDC bytes and scans for the substring `cus`. Once **5 + N** bytes are available from the start of that magic, it consumes one frame and forwards the **N** payload bytes to the parser task.

### 1.1 Framing caveats

- **Magic must not be split across CDC transfers** in a way that prevents detection: the parser looks for the contiguous ASCII sequence `cus`. Normal practice is to send the **entire frame in one `write()`** or ensure the magic appears intact after concatenation of chunks.
- Payload bytes may be any UTF-8 octets. Avoid embedding a **NUL** (0x00) inside the JSON unless your toolchain intentionally sends binary; the firmware NUL-terminates a copy for `cJSON_Parse`, so **NUL inside the payload truncates the JSON**.
- Accidental appearance of the bytes `cus` inside UTF-8 text in another protocol could theoretically confuse the scanner if those bytes were fed through the same buffer; keep host traffic aligned to the intended frame types.

---

## 2. JSON schema (payload)

All geometry is in **pixels** on the **drawable canvas** only (§0): **origin `(0,0)` is the top-left of that canvas** (on the reference device: the top-left of the **480×200** top band). Values are **not** relative to the full LCD.


| Field       | Type        | Required | Description                                                                                                                                                                                                                   |
| ----------- | ----------- | -------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `x`         | integer     | no       | Distance from the **left edge of the drawable canvas** (default `0`).                                                                                                                                                        |
| `y`         | integer     | no       | Distance from the **top edge of the drawable canvas** (default `0`).                                                                                                                                                          |
| `w`         | integer     | no       | Panel width (default **`LV_HOR_RES`** — full canvas width, e.g. **480**).                                                                                                                                                      |
| `h`         | integer     | no       | Panel height (default **`LV_VER_RES − 120`** — full canvas height, e.g. **200** on a 320-tall display).                                                                                                                         |
| `text`      | string      | no       | UTF-8 label text for **one new stacked panel** (default empty). **Empty or omitted** → **no new panel** is added this frame (unless you only use `clear_canvas`). See §2.4.                                                     |
| `fg`        | string      | no       | Foreground `#RRGGBB` (default `#FFFFFF`). Invalid format logs a warning and falls back to default.                                                                                                                            |
| `bg`        | string      | no       | Background `#RRGGBB` for the **panel** behind the text (default `#000000`). Invalid → default.                                                                                                                                |
| `align`     | string      | no       | Horizontal text alignment (default `LEFT`). See §2.1.                                                                                                                                                                         |
| `long_mode` | string      | no       | LVGL label long mode (default `WRAP`). See §2.2.                                                                                                                                                                              |
| `activate`  | bool or int | no       | If **true** / non-zero (default **true**), firmware switches to the customised app after updating widgets. If **false** / `0`, updates apply while **off-screen** until the user opens the customised mode (e.g. via rotary). |
| `border`    | bool or int | no       | If the key is **absent**, no outline is drawn (`border-width` 0). If **present**: `false` or number **`≤ 0`** → no border; **`true`** → **1** px border; **integer `> 0`** → border width in pixels. See §2.3.                |
| `border-color` | string   | no       | Outline colour `#RRGGBB`. **Only used when `border` draws a border**; if `border` is on but this key is omitted, firmware uses **`#888888`**. Ignored when there is no border. Invalid hex → warning, colour ignored (default grey still used when border on). |
| `border-radius` | integer | no       | Corner radius of the **new panel** in **pixels**. If **omitted**, new panels use LVGL default (**0** — square corners). If **present** (including **`0`**), sets `radius` on that panel only. |
| `clear_canvas` | bool or int | no | If **true** / non-zero: **remove every stacked text panel** from the drawable canvas (§3). Does **not** remove the key strip. May be combined with a non-empty **`text`** in the **same** frame (clear first, then draw one new panel). If **false** / absent / **0**, the canvas is left as-is before applying **`text`**. |


**Font:** There is no host-selectable font field. The customised label uses the same embedded **`regular`** font as the micropad key captions (Chinese-capable glyph set in your project’s `fonts/` build), not **`LV_FONT_DEFAULT`**.

### 2.1 `align` (case-insensitive)

Maps to LVGL 8 text alignment:


| Value    | Meaning |
| -------- | ------- |
| `LEFT`   | Left    |
| `CENTER` | Center  |
| `RIGHT`  | Right   |
| `AUTO`   | Auto    |


Unknown values behave like `LEFT`.

### 2.2 `long_mode` (case-insensitive)

Maps to LVGL 8 label long mode:


| Value             | Meaning             |
| ----------------- | ------------------- |
| `WRAP`            | Wrap                |
| `SCROLL`          | Scroll horizontally |
| `SCROLL_CIRCULAR` | Circular scroll     |
| `CLIP`            | Clip                |
| `DOT`             | Ellipsis            |


Unknown values behave like `WRAP`.

### 2.3 `border`, `border-color`, `border-radius`

These keys style the **panel** rectangle (`x`, `y`, `w`, `h`), not individual glyphs. They are **independent**: e.g. you may set `border-radius` without `border` to get a rounded background only.

### 2.4 Stacking, **`text`**, and **`clear_canvas`**

- Each frame may **`clear_canvas`** (optional), then optionally **append one new text panel** if **`text`** is a **non-empty** UTF-8 string (after JSON parsing). **Empty `text` or omitted `text`** does **not** add a panel — use that for `{"clear_canvas":true}` without drawing.
- **Later frames do not erase earlier panels**: new panels are stacked in creation order (newer panels paint above older ones where they overlap). Only **`clear_canvas`** removes all host-drawn panels at once.
- To reset the canvas to blank (aside from the fixed key strip), send e.g. `{"clear_canvas":true,"activate":true}` (no `text`).
- The firmware keeps at most **48** stacked panels; when exceeded, the **oldest** panels are deleted first (`CUS_MAX_CANVAS_OVERLAYS` in `[customised_app.c](../main/customised_app.c)`).

---

## 3. Behaviour summary

- The customised LVGL screen is: **drawable canvas** (§0) containing **zero or more** stacked host-driven **panels** (each with one **label**), plus the bottom **key strip**.
- With `**activate: true`**, the device switches to `**e_app_customised**` after handling the frame (same as before).
- With `**activate: false**`, canvas updates apply off-screen until the user opens customised mode manually.

---

## 4. Building a frame (examples)

### 4.1 Python 3 (pyserial)

```python
import json
import serial

def build_cus_frame(obj: dict) -> bytes:
    payload = json.dumps(obj, separators=(",", ":")).encode("utf-8")
    n = len(payload)
    if n < 1 or n > 2048:
        raise ValueError(f"payload length {n} out of range")
    return b"cus" + n.to_bytes(2, "big") + payload

ser = serial.Serial("COM5", 115200, timeout=1)  # adjust port / baud to match device
# On 480x320, custom-area height is 200 px; keep y+h <= 200 (plus clamping in firmware).
frame = build_cus_frame({
    "x": 10,
    "y": 10,
    "w": 460,
    "h": 80,
    "text": "Hello — UTF-8 ok",
    "fg": "#FFFFFF",
    "bg": "#224466",
    "align": "CENTER",
    "long_mode": "SCROLL",
    "activate": True,
})
ser.write(frame)
ser.flush()
```

### 4.2 Minimal JSON examples

Fill the **custom area** on a 480×320 panel (omit `w`/`h` for the same effect):

```json
{"x":0,"y":0,"w":480,"h":200,"text":"Loading…","fg":"#EEEEEE","bg":"#111111","align":"CENTER","long_mode":"WRAP","activate":true}
```

Panel with **1 px** border, custom colour, and **8 px** rounded corners:

```json
{"x":8,"y":8,"w":464,"h":120,"text":"Status","border":true,"border-color":"#00AAFF","border-radius":8,"activate":true}
```

Clear all host-drawn panels on the canvas (keys unchanged):

```json
{"clear_canvas":true,"activate":true}
```

Clear then draw a single new line (same frame):

```json
{"clear_canvas":true,"x":0,"y":0,"w":480,"h":40,"text":"Fresh start","activate":true}
```

Update text only (stay on current app until user rotates):

```json
{"text":"Queued message","activate":false}
```

---

## 5. Limits checklist


| Limit                             | Value                                      | Source                                         |
| --------------------------------- | ------------------------------------------ | ---------------------------------------------- |
| Drawable canvas (reference 480×320 LCD) | **200 × 480** px (**200** tall × **480** wide) only | `CUS_TOP_H` × `LV_HOR_RES` in `customised_app.c` |
| Max stacked text panels on canvas | **48**                                     | `CUS_MAX_CANVAS_OVERLAYS` in `customised_app.c` |
| Canvas height (general)           | **`LV_VER_RES − 120`** px                  | `CUS_TOP_H` in `customised_app.c`              |
| Key strip height                  | **120** px (fixed)                         | `CUS_BOTTOM_H` in `customised_app.c`           |
| Max payload length **N** (parser) | 2048 bytes                                 | `PARSER_MAX_DATA_LEN`                          |
| Practical max **N** per frame     | ≤ 2043                                     | `MAX_PRO_BUF_LEN` − 5 byte header              |
| Typical CDC chunk size            | Host-dependent                             | Sending whole frames avoids split-magic issues |


---

## 6. Related protocols on the same CDC interface

Only documented here for isolation—do **not** mix bytes arbitrarily:

- `**ebf`** + 1-byte length + payload: keyboard / configuration commands (device-specific).
- `**pcs**` + 2-byte big-endian length + payload: PC monitor JSON (different consumer).

Host software for customised UI should send **only well-formed `cus` frames** when targeting this feature.

---

## 7. Verification tips

1. Open the correct virtual COM port created by USB (VID/PID depend on device firmware).
2. Send one complete `cus` frame with `"activate": true` and distinct `fg`/`bg` to confirm the customised screen appears.
3. Cycle modes on the device (rotary) to confirm micropad → monitor → customised order when using `**activate`: false** updates.

If parsing fails, the firmware logs a warning (tag `cus_app`, JSON parse failure); check UTF-8 validity and JSON syntax.