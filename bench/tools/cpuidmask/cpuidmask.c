// Benchmark-only tool: hides CPU features (AVX-512, AMX, VNNI) from one process so a server CPU
// behaves like an AVX2 laptop for kernel selection. Loaded with LD_PRELOAD. It turns on Linux
// CPUID faulting for the process and answers each trapped CPUID with the real answer minus the
// masked feature bits. Profile comes from the CPUIDMASK variable: "avx2" (default) hides AVX-512,
// AMX and every VNNI flavour; "avx2-vnni" keeps AVX-VNNI; "none" leaves the CPU as it is.
#define _GNU_SOURCE
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ucontext.h>
#include <unistd.h>
#include <sys/syscall.h>
#include <cpuid.h>
#include <dlfcn.h>

#define ARCH_GET_CPUID 0x1011
#define ARCH_SET_CPUID 0x1012

static int keep_avx_vnni = 0;
static int active = 0;
static struct sigaction previous;
static int (*real_sigaction)(int, const struct sigaction *, struct sigaction *);

static void answer(unsigned leaf, unsigned sub, unsigned *a, unsigned *b, unsigned *c, unsigned *d)
{
    __cpuid_count(leaf, sub, *a, *b, *c, *d);
    if (leaf == 7 && sub == 0) {
        *b &= ~((1u << 16) | (1u << 17) | (1u << 21) | (1u << 26) | (1u << 27) | (1u << 28) | (1u << 30) | (1u << 31));
        *c &= ~((1u << 1) | (1u << 6) | (1u << 11) | (1u << 12) | (1u << 14));
        *d &= ~((1u << 2) | (1u << 3) | (1u << 8) | (1u << 22) | (1u << 23) | (1u << 24) | (1u << 25));
    } else if (leaf == 7 && sub == 1) {
        unsigned keep = keep_avx_vnni ? (1u << 4) : 0;
        *a &= ~(((1u << 4) | (1u << 5) | (1u << 21) | (1u << 23)) & ~keep);
        *d &= ~((1u << 4) | (1u << 5) | (1u << 10) | (1u << 19));
    }
}

static void on_fault(int sig, siginfo_t *info, void *context)
{
    ucontext_t *uc = (ucontext_t *)context;
    unsigned char *ip = (unsigned char *)uc->uc_mcontext.gregs[REG_RIP];
    if (ip[0] == 0x0F && ip[1] == 0xA2) {
        unsigned a, b, c, d;
        syscall(SYS_arch_prctl, ARCH_SET_CPUID, 1UL);
        answer((unsigned)uc->uc_mcontext.gregs[REG_RAX], (unsigned)uc->uc_mcontext.gregs[REG_RCX], &a, &b, &c, &d);
        syscall(SYS_arch_prctl, ARCH_SET_CPUID, 0UL);
        uc->uc_mcontext.gregs[REG_RAX] = a;
        uc->uc_mcontext.gregs[REG_RBX] = b;
        uc->uc_mcontext.gregs[REG_RCX] = c;
        uc->uc_mcontext.gregs[REG_RDX] = d;
        uc->uc_mcontext.gregs[REG_RIP] += 2;
        return;
    }
    // Not a CPUID: hand the fault to whoever was registered before, or die the default way.
    if (previous.sa_flags & SA_SIGINFO) {
        if (previous.sa_sigaction) { previous.sa_sigaction(sig, info, context); return; }
    } else if (previous.sa_handler != SIG_DFL && previous.sa_handler != SIG_IGN) {
        previous.sa_handler(sig); return;
    }
    // Nobody to forward to: restore the default action and let the fault recur.
    struct sigaction standard;
    memset(&standard, 0, sizeof standard);
    standard.sa_handler = SIG_DFL;
    real_sigaction(SIGSEGV, &standard, NULL);
}

// A runtime that installs its own SIGSEGV handler (the .NET runtime does, for null checks) must
// not displace this one, or the next CPUID would kill the process. So while the mask is active a
// request to handle SIGSEGV is recorded as the handler to forward to, and this one stays first.
int sigaction(int sig, const struct sigaction *action, struct sigaction *old)
{
    if (!real_sigaction) real_sigaction = (int (*)(int, const struct sigaction *, struct sigaction *))dlsym(RTLD_NEXT, "sigaction");
    if (sig != SIGSEGV || !active) return real_sigaction(sig, action, old);
    if (old) *old = previous;
    if (action) previous = *action;
    return 0;
}

__attribute__((constructor)) static void start(void)
{
    real_sigaction = (int (*)(int, const struct sigaction *, struct sigaction *))dlsym(RTLD_NEXT, "sigaction");
    const char *profile = getenv("CPUIDMASK");
    if (profile && strcmp(profile, "none") == 0) return;
    keep_avx_vnni = profile && strcmp(profile, "avx2-vnni") == 0;
    struct sigaction action;
    memset(&action, 0, sizeof action);
    action.sa_sigaction = on_fault;
    action.sa_flags = SA_SIGINFO | SA_NODEFER | SA_ONSTACK;
    real_sigaction(SIGSEGV, &action, &previous);
    if (syscall(SYS_arch_prctl, ARCH_SET_CPUID, 0UL) != 0) {
        fprintf(stderr, "cpuidmask: CPUID faulting is not available on this machine; features are NOT masked.\n");
        real_sigaction(SIGSEGV, &previous, NULL);
        return;
    }
    active = 1;
}
