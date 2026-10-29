/*
 * WebcamLab Phase 12: Independent Linux UVC Control Investigation
 * Target Hardware: Dell UZ2315H Monitor Integrated Webcam (0BDA:58BA)
 *
 * Compiles with:
 *   gcc -O2 -Wall uvc_xu_query.c -o uvc_xu_query
 *
 * Usage:
 *   ./uvc_xu_query [/dev/video0]
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <fcntl.h>
#include <unistd.h>
#include <errno.h>
#include <sys/ioctl.h>
#include <linux/types.h>
#include <linux/uvcvideo.h>
#include <linux/usb/video.h>

#define TARGET_DEV_DEFAULT "/dev/video0"

static void print_hex(const char *label, const unsigned char *buf, int len) {
    printf("%s", label);
    for (int i = 0; i < len; i++) {
        printf("%02X ", buf[i]);
    }
    printf("\n");
}

static int query_xu(int fd, __u8 unit, __u8 selector, __u8 query, __u16 size, void *data) {
    struct uvc_xu_control_query q;
    memset(&q, 0, sizeof(q));
    q.unit = unit;
    q.selector = selector;
    q.query = query;
    q.size = size;
    q.data = (__u8 *)data;

    if (ioctl(fd, UVCIOC_CTRL_QUERY, &q) < 0) {
        return -errno;
    }
    return 0;
}

static void probe_selector(int fd, __u8 unit, __u8 selector, const char *unit_name) {
    __u16 len = 0;
    int ret;

    // 1. Query Length
    ret = query_xu(fd, unit, selector, UVC_GET_LEN, sizeof(len), &len);
    if (ret != 0) {
        // Selector unsupported or non-existent
        return;
    }

    printf("  [%s Selector %2u] Len: %u bytes", unit_name, selector, len);

    // 2. Query Info Flags
    __u8 info = 0;
    ret = query_xu(fd, unit, selector, UVC_GET_INFO, sizeof(info), &info);
    if (ret == 0) {
        printf(" | Caps: [");
        if (info & 0x01) printf("GET ");
        if (info & 0x02) printf("SET ");
        if (info & 0x04) printf("RES ");
        if (info & 0x08) printf("AUTO ");
        printf("] (0x%02X)", info);
    }
    printf("\n");

    // 3. Query Current Value (Read-Only)
    if (len > 0 && len <= 256) {
        unsigned char *buf = (unsigned char *)calloc(1, len);
        ret = query_xu(fd, unit, selector, UVC_GET_CUR, len, buf);
        if (ret == 0) {
            print_hex("         GET_CUR: ", buf, len);
        } else {
            printf("         GET_CUR: Error (%s)\n", strerror(-ret));
        }

        // 4. Query Default Value
        ret = query_xu(fd, unit, selector, UVC_GET_DEF, len, buf);
        if (ret == 0) {
            print_hex("         GET_DEF: ", buf, len);
        }
        free(buf);
    } else if (len == 0) {
        printf("         GET_CUR: (0-byte Action/Trigger selector)\n");
    }
}

int main(int argc, char **argv) {
    const char *dev = TARGET_DEV_DEFAULT;
    if (argc > 1) {
        dev = argv[1];
    }

    printf("==================================================================\n");
    printf("  WebcamLab Phase 12: Linux Native UVC Extension Unit Interrogation\n");
    printf("  Device: %s\n", dev);
    printf("  Interface: Linux UVCIOC_CTRL_QUERY ioctl\n");
    printf("  Mode: Strict Read-Only (GET_LEN, GET_INFO, GET_CUR, GET_DEF)\n");
    printf("==================================================================\n\n");

    int fd = open(dev, O_RDWR);
    if (fd < 0) {
        fprintf(stderr, "[ERROR] Unable to open device %s: %s\n", dev, strerror(errno));
        return 1;
    }

    // Interrogate Unit 4 (Realtek Extended ISP Engine: GUID {1229a78c-47b4-4094-b0ce-db07386fb938})
    printf("--- Interrogating Unit 4 (Node 3, Realtek Extended ISP Engine) ---\n");
    for (__u8 sel = 0; sel <= 15; sel++) {
        probe_selector(fd, 4, sel, "Unit 4");
    }
    printf("\n");

    // Interrogate Unit 7 (Realtek Calibration Header & Flag Node: GUID {0fb885c3-68c2-4547-90f7-8f47579d95fc})
    printf("--- Interrogating Unit 7 (Node 4, Realtek Calibration & Flags) ---\n");
    for (__u8 sel = 0; sel <= 10; sel++) {
        probe_selector(fd, 7, sel, "Unit 7");
    }
    printf("\n");

    close(fd);
    printf("==================================================================\n");
    printf("  Interrogation Complete (Strict Read-Only Mode Maintained)\n");
    printf("==================================================================\n");
    return 0;
}
