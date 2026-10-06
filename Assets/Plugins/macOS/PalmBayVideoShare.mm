#import <AppKit/AppKit.h>

static NSSharingServicePicker *palmBayReplayPicker;

extern "C" __attribute__((visibility("default"))) int PalmBayShareVideo(const char *utf8Path)
{
    if (utf8Path == nullptr) return 0;
    @autoreleasepool {
        NSString *path = [NSString stringWithUTF8String:utf8Path];
        if (path == nil || ![[NSFileManager defaultManager] isReadableFileAtPath:path]) return 0;
        __block int result = 0;
        void (^showPicker)(void) = ^{
            NSWindow *window = [NSApp keyWindow] ?: [NSApp mainWindow];
            NSView *view = [window contentView];
            if (view == nil) return;
            NSURL *video = [NSURL fileURLWithPath:path];
            palmBayReplayPicker = [[NSSharingServicePicker alloc] initWithItems:@[video]];
            NSRect bounds = [view bounds];
            NSRect anchor = NSMakeRect(NSMidX(bounds), NSMidY(bounds), 1, 1);
            [palmBayReplayPicker showRelativeToRect:anchor ofView:view preferredEdge:NSRectEdgeMaxY];
            result = 1;
        };
        if ([NSThread isMainThread]) showPicker();
        else dispatch_sync(dispatch_get_main_queue(), showPicker);
        return result;
    }
}
