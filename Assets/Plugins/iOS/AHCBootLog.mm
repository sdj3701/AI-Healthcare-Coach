#import <Foundation/Foundation.h>

extern "C" void AHCBootLogNative(const char* message)
{
    if (message == NULL)
    {
        return;
    }

    NSLog(@"[AHC] %s", message);
}
