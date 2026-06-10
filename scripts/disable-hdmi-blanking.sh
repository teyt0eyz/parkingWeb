#!/bin/bash
# ปิดการพักหน้าจอ/DPMS บนจอ HDMI ของ Raspberry Pi (กันจอดำระหว่างเล่นวิดีโอ kiosk)
# รองรับทั้ง X11 (LXDE) และ Wayland (wayfire / labwc)
#
# วิธีใช้:
#   chmod +x scripts/disable-hdmi-blanking.sh
#   ./scripts/disable-hdmi-blanking.sh
#   sudo reboot

set -e

echo "== ปิด console blanking ระดับ kernel =="
CMDLINE_FILE="/boot/firmware/cmdline.txt"
[ -f "$CMDLINE_FILE" ] || CMDLINE_FILE="/boot/cmdline.txt"

if [ -f "$CMDLINE_FILE" ]; then
    if grep -qw "consoleblank=0" "$CMDLINE_FILE"; then
        echo "  - consoleblank=0 มีอยู่แล้วใน $CMDLINE_FILE"
    else
        sudo cp "$CMDLINE_FILE" "$CMDLINE_FILE.bak"
        sudo sed -i 's/$/ consoleblank=0/' "$CMDLINE_FILE"
        echo "  - เพิ่ม consoleblank=0 ใน $CMDLINE_FILE (สำรองไว้ที่ $CMDLINE_FILE.bak)"
    fi
else
    echo "  - ไม่พบ cmdline.txt ข้ามขั้นตอนนี้"
fi

SESSION_TYPE="${XDG_SESSION_TYPE:-unknown}"
echo ""
echo "== ตรวจพบ session type: $SESSION_TYPE =="

if [ "$SESSION_TYPE" = "wayland" ]; then
    if [ -f "$HOME/.config/wayfire.ini" ] || command -v wayfire >/dev/null 2>&1; then
        echo "== ตั้งค่า wayfire (Wayland) =="
        WAYFIRE_INI="$HOME/.config/wayfire.ini"
        mkdir -p "$(dirname "$WAYFIRE_INI")"
        touch "$WAYFIRE_INI"
        if grep -q "^\[idle\]" "$WAYFIRE_INI" 2>/dev/null; then
            if grep -q "^dpms_timeout" "$WAYFIRE_INI"; then
                sed -i 's/^dpms_timeout.*/dpms_timeout = -1/' "$WAYFIRE_INI"
            else
                sed -i '/^\[idle\]/a dpms_timeout = -1' "$WAYFIRE_INI"
            fi
        else
            printf '\n[idle]\ndpms_timeout = -1\n' >> "$WAYFIRE_INI"
        fi
        echo "  - ตั้ง dpms_timeout = -1 ใน $WAYFIRE_INI"
    else
        echo "== ตั้งค่า labwc (Wayland) =="
        LABWC_RC="$HOME/.config/labwc/rc.xml"
        if [ -f "$LABWC_RC" ]; then
            echo "  - พบ $LABWC_RC กรุณาตรวจสอบ <screensaver><idle>0</idle></screensaver> ด้วยตนเอง"
        else
            echo "  - ไม่พบ wayfire.ini หรือ labwc rc.xml กรุณาตรวจสอบ compositor ที่ใช้งานด้วยตนเอง"
        fi
    fi
elif [ "$SESSION_TYPE" = "x11" ]; then
    echo "== ตั้งค่า X11 / LXDE autostart =="
    AUTOSTART="$HOME/.config/lxsession/LXDE-pi/autostart"
    mkdir -p "$(dirname "$AUTOSTART")"
    touch "$AUTOSTART"

    for line in "@xset s off" "@xset s noblank" "@xset -dpms"; do
        if grep -qF "$line" "$AUTOSTART"; then
            echo "  - มีอยู่แล้ว: $line"
        else
            echo "$line" >> "$AUTOSTART"
            echo "  - เพิ่ม: $line"
        fi
    done
    echo "  - บันทึกที่ $AUTOSTART"
else
    echo "  - ไม่ทราบ session type ($SESSION_TYPE) กรุณาตั้งค่าด้วยตนเองตามคำแนะนำ"
fi

echo ""
echo "เสร็จแล้ว — รีบูตเครื่องเพื่อให้มีผล: sudo reboot"
