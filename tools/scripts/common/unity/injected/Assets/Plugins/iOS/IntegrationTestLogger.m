#import <os/log.h>

void LogToUnifiedSystem(const char* msg) {
    os_log_with_type(OS_LOG_DEFAULT, OS_LOG_TYPE_DEFAULT, "%{public}s", msg);
}
