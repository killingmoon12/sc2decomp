#!/usr/bin/env bash
#
# decompile_survivalcraft.sh
#
# Extract and C#-decompile the managed assemblies of the Survivalcraft 2 APK
# (.NET for Android / Xamarin). The APK keeps its DLLs in a native ELF
# container: lib/<abi>/libassemblies.<abi>.blob.so holds an "XABA" assembly
# store whose entries are LZ4-block-compressed "XALZ" PE images.
#
# The APK itself is NEVER modified - everything is written to an output
# directory (default: ./survivalcraft_decompiled).
#
# Requirements: bash, unzip, python3 (stdlib only), dotnet SDK (for ilspycmd).
#
# Usage:
#   ./decompile_survivalcraft.sh [options] [path/to/app.apk]
#
# Options:
#   -o, --output DIR   Output directory (default: ./survivalcraft_decompiled)
#   --all              Decompile every assembly (framework/BCL included),
#                      otherwise only the application assemblies are decompiled
#   -h, --help         Show this help
#
set -euo pipefail

OUTPUT_DIR="survivalcraft_decompiled"
DECOMP_ALL=0
APK=""

usage() { sed -n '2,23p' "$0" | sed 's/^# \{0,1\}//'; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        -o|--output) OUTPUT_DIR="$2"; shift 2 ;;
        --all)      DECOMP_ALL=1; shift ;;
        -h|--help)  usage; exit 0 ;;
        -*)         echo "Unknown option: $1" >&2; usage; exit 1 ;;
        *)          APK="$1"; shift ;;
    esac
done

if [[ -z "$APK" ]]; then
    APK=$(find . -maxdepth 1 -name '*.apk' -print -quit)
    [[ -n "$APK" ]] || { echo "No APK given and no *.apk found in the current directory." >&2; exit 1; }
fi
[[ -f "$APK" ]] || { echo "APK not found: $APK" >&2; exit 1; }

ROOT="$OUTPUT_DIR"
EXTRACT_DIR="$ROOT/extracted"
ASM_DIR="$ROOT/assemblies"
DECOMP_DIR="$ROOT/decompiled"
TOOLS_DIR="$ROOT/tools"

step() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }

command -v unzip   >/dev/null || { echo "missing dependency: unzip"   >&2; exit 1; }
command -v python3 >/dev/null || { echo "missing dependency: python3" >&2; exit 1; }

step "APK: $APK"
mkdir -p "$EXTRACT_DIR" "$ASM_DIR" "$DECOMP_DIR" "$TOOLS_DIR"

# ---------------------------------------------------------------------------
# 1. Extract the assembly store container out of the APK (nothing else needed)
# ---------------------------------------------------------------------------
step "Extracting assembly store from APK"
STORE_SO=$(unzip -Z1 "$APK" | grep -E '^lib/[^/]+/libassemblies\.[^/]+\.blob\.so$' | head -n 1 || true)

if [[ -n "$STORE_SO" ]]; then
    unzip -o -q "$APK" "$STORE_SO" -d "$EXTRACT_DIR"
    echo "store container: $STORE_SO"
else
    # Older Xamarin builds keep plain assemblies in the zip
    echo "No XABA store found - looking for plain assemblies/ entries"
    if unzip -Z1 "$APK" | grep -qE '^assemblies/.+\.dll$'; then
        unzip -o -q "$APK" 'assemblies/*.dll' -d "$EXTRACT_DIR"
        cp "$EXTRACT_DIR"/assemblies/*.dll "$ASM_DIR"/
        echo "extracted $(ls -1 "$ASM_DIR" | wc -l) assemblies (raw)"
    else
        echo "No managed assemblies found in APK." >&2; exit 1
    fi
fi

# ---------------------------------------------------------------------------
# 2. Parse the ELF 'payload' section -> XABA header -> descriptors -> LZ4
#    and write each assembly as a .dll file
# ---------------------------------------------------------------------------
if [[ -f "$EXTRACT_DIR/$STORE_SO" ]]; then
    step "Decoding XABA assembly store (ELF payload -> LZ4 -> PE images)"
    python3 - "$EXTRACT_DIR/$STORE_SO" "$ASM_DIR" <<'PYEOF'
import os, struct, sys

store_path, out_dir = sys.argv[1], sys.argv[2]

def lz4_block_decode(src, expected_size=None):
    """Decode a raw LZ4 block (no frame header)."""
    out = bytearray()
    i, n = 0, len(src)
    while i < n:
        token = src[i]; i += 1
        lit_len = token >> 4
        if lit_len == 15:
            while True:
                b = src[i]; i += 1
                lit_len += b
                if b != 255:
                    break
        out += src[i:i + lit_len]; i += lit_len
        if i >= n:                      # last sequence: literals only
            break
        offset = src[i] | (src[i + 1] << 8); i += 2
        if offset == 0 or offset > len(out):
            raise ValueError(f"invalid LZ4 match offset {offset} at output {len(out)}")
        match_len = (token & 15) + 4
        if (token & 15) == 15:
            while True:
                b = src[i]; i += 1
                match_len += b
                if b != 255:
                    break
        start = len(out) - offset
        for k in range(match_len):      # overlap-safe byte copy
            out.append(out[start + k])
        if expected_size and len(out) > expected_size:
            raise ValueError("decoded size exceeds declared size")
    return bytes(out)

def get_payload_section(data):
    """Return (offset, size) of the section named 'payload' in an ELF64 LE file."""
    if data[:4] != b"\x7fELF":
        return None
    if data[4] != 2 or data[5] != 1:    # class, endianness
        raise ValueError("only 64-bit little-endian ELF is supported")
    e_shoff     = struct.unpack_from("<Q", data, 0x28)[0]
    e_shentsize = struct.unpack_from("<H", data, 0x3A)[0]
    e_shnum     = struct.unpack_from("<H", data, 0x3C)[0]
    e_shstrndx  = struct.unpack_from("<H", data, 0x3E)[0]
    sh = lambda idx: e_shoff + idx * e_shentsize
    str_off = struct.unpack_from("<Q", data, sh(e_shstrndx) + 0x18)[0]
    for i in range(e_shnum):
        base = sh(i)
        name_off = struct.unpack_from("<I", data, base)[0]
        end = data.index(b"\0", str_off + name_off)
        name = data[str_off + name_off:end]
        if name == b"payload":
            return (struct.unpack_from("<Q", data, base + 0x18)[0],
                    struct.unpack_from("<Q", data, base + 0x20)[0])
    return None

data = open(store_path, "rb").read()

if data[:4] == b"XABA":
    payload, poff = data, 0
else:
    sec = get_payload_section(data)
    if sec is None:
        sys.exit("error: no XABA store and no 'payload' section found")
    poff, size = sec
    payload = data[poff:poff + size]
    if payload[:4] != b"XABA":
        sys.exit("error: 'payload' section does not start with XABA magic")

magic, version, entry_count, index_entry_count, index_size = struct.unpack_from("<5I", payload, 0)
if magic != 0x41424158:
    sys.exit(f"error: bad store magic {magic:#x}")
print(f"store format version {version & 0x0FFFFFFF:#x} (raw {version:#x}), "
      f"{entry_count} assemblies, index {index_size} bytes")

pos = 20 + index_size                                   # skip header + name index
descs = [struct.unpack_from("<7I", payload, pos + i * 28) for i in range(entry_count)]
pos += entry_count * 28

names = []
for _ in range(entry_count):
    (length,) = struct.unpack_from("<I", payload, pos); pos += 4
    names.append(payload[pos:pos + length].decode("utf-8")); pos += length

written = 0
for i, (idx, off, size, dbg_off, dbg_size, cfg_off, cfg_size) in enumerate(descs):
    blob = payload[off:off + size]
    if blob[:4] == b"XALZ":                              # LZ4-compressed image
        aidx, usize = struct.unpack_from("<II", blob, 4)
        if aidx != i:
            sys.exit(f"error: descriptor index mismatch for {names[i]}")
        image = lz4_block_decode(blob[12:], usize)
        if len(image) != usize:
            sys.exit(f"error: size mismatch for {names[i]}: {len(image)} != {usize}")
    else:
        image = blob                                     # stored uncompressed
    if image[:2] != b"MZ":
        sys.exit(f"error: {names[i]} is not a valid PE image")
    out_path = os.path.join(out_dir, names[i])
    with open(out_path, "wb") as fh:
        fh.write(image)
    written += 1

print(f"extracted {written}/{entry_count} assemblies -> {out_dir}")
print("first entries:", ", ".join(names[:5]))
PYEOF
fi

# ---------------------------------------------------------------------------
# 3. Make sure a C# decompiler (ilspycmd) is available
# ---------------------------------------------------------------------------
step "Preparing C# decompiler (ilspycmd)"
export DOTNET_ROLL_FORWARD=Major          # let tools built for older TFMs run
ILSPYCMD=""
if command -v ilspycmd >/dev/null; then
    ILSPYCMD=$(command -v ilspycmd)
elif [[ -x "$TOOLS_DIR/ilspycmd" ]]; then
    ILSPYCMD="$TOOLS_DIR/ilspycmd"
elif command -v dotnet >/dev/null; then
    dotnet tool install --tool-path "$TOOLS_DIR" ilspycmd >/dev/null
    ILSPYCMD="$TOOLS_DIR/ilspycmd"
else
    echo "error: neither ilspycmd nor dotnet SDK found" >&2; exit 1
fi
echo "using: $ILSPYCMD ($("$ILSPYCMD" --version 2>/dev/null | head -n 1))"

# ---------------------------------------------------------------------------
# 4. Decompile
# ---------------------------------------------------------------------------
step "Decompiling C# sources"
mapfile -t DLLS < <(find "$ASM_DIR" -maxdepth 1 -name '*.dll' | sort)

is_framework() {
    case "$(basename "$1")" in
        System*|Microsoft*|Mono*|mscorlib*|netstandard*|WindowsBase*|Presentation*|\
        Accessibility*|CustomMarshalers*|IKVM*|_Microsoft.Android*)
            return 0 ;;
        *) return 1 ;;
    esac
}

count=0
for dll in "${DLLS[@]}"; do
    base=$(basename "$dll" .dll)
    if [[ $DECOMP_ALL -eq 0 ]] && is_framework "$dll"; then
        continue
    fi
    out="$DECOMP_DIR/$base"
    mkdir -p "$out"
    echo "  - $base"
    if ! "$ILSPYCMD" -p -o "$out" "$dll" >/dev/null 2>"$ROOT/.ilspy.err"; then
        echo "      (decompilation failed, see $ROOT/.ilspy.err)" >&2
        continue
    fi
    count=$((count + 1))
done

# ---------------------------------------------------------------------------
# 5. Summary
# ---------------------------------------------------------------------------
step "Done"
echo "assemblies : $ASM_DIR ($(find "$ASM_DIR" -name '*.dll' | wc -l) DLLs)"
echo "sources    : $DECOMP_DIR ($count assemblies decompiled)"
if [[ -d "$DECOMP_DIR/Survivalcraft" ]]; then
    cs_total=$(find "$DECOMP_DIR/Survivalcraft" -name '*.cs' | wc -l)
    echo
    echo "Survivalcraft.dll -> $cs_total decompiled .cs files:"
    find "$DECOMP_DIR/Survivalcraft" -name '*.cs' | sort | sed 's|^|    |' | head -n 30
fi
