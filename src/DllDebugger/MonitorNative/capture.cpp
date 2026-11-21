#include <windows.h>
#include <dbgeng.h>
#include <psapi.h>
#include <wrl/client.h>
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdio>
#include <filesystem>
#include <iostream>
#include <map>
#include <optional>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

using Microsoft::WRL::ComPtr;
using Word = ULONG64;
using Clock = std::chrono::steady_clock;
constexpr bool wide = sizeof(void*) == 8;

static Word timestamp() {
    FILETIME value;
    GetSystemTimePreciseAsFileTime(&value);
    return (Word(value.dwHighDateTime) << 32) | value.dwLowDateTime;
}

static void check(HRESULT hr, const char* operation) {
    if (FAILED(hr)) {
        std::ostringstream message;
        message << operation << " (0x" << std::hex << static_cast<unsigned>(hr) << ")";
        throw std::runtime_error(message.str());
    }
}

struct Function {
    std::filesystem::path image;
    Word rva;
    std::wstring widths; // argument slots, 4 or 8 bytes; '?' suppresses decoding
    bool result64;
    Word base = 0;
};
struct Pending {
    Word id, address, stack;
    ULONG thread;
    size_t function;
    Clock::time_point entered;
};

class Capture {
    ComPtr<IDebugRegisters> regs;
    ComPtr<IDebugDataSpaces> memory;
    ComPtr<IDebugSymbols3> symbols;
    ComPtr<IDebugSystemObjects> system;
    std::map<Word, IDebugBreakpoint*> points;
    std::vector<Pending> calls;
    Word sequence = 0;

    std::optional<Word> read(Word address, ULONG size) {
        Word value = 0;
        ULONG count = 0;
        if (FAILED(memory->ReadVirtual(address, &value, size, &count)) || count != size) return {};
        return value;
    }
    std::optional<Word> reg(const char* name) {
        ULONG index;
        DEBUG_VALUE value{};
        if (FAILED(regs->GetIndexByName(name, &index)) || FAILED(regs->GetValue(index, &value))) return {};
        return value.Type == DEBUG_VALUE_INT64 ? value.I64 : value.I32;
    }
    static std::string number(std::optional<Word> n) { return n ? std::to_string(*n) : "?"; }

    void point(Word address) {
        if (points.count(address)) return;
        IDebugBreakpoint* bp;
        check(control->AddBreakpoint(DEBUG_BREAKPOINT_CODE, DEBUG_ANY_ID, &bp), "AddBreakpoint");
        check(bp->SetOffset(address), "SetOffset");
        check(bp->AddFlags(DEBUG_BREAKPOINT_ENABLED), "Enable breakpoint");
        points.emplace(address, bp);
    }
    void prune() {
        for (auto it = points.begin(); it != points.end();) {
            auto address = it->first;
            bool used = std::any_of(functions.begin(), functions.end(), [=](auto& f) { return f.base && f.base + f.rva == address; })
                || std::any_of(calls.begin(), calls.end(), [=](auto& call) { return call.address == address; });
            if (used) ++it;
            else {
                check(control->RemoveBreakpoint(it->second), "RemoveBreakpoint");
                it = points.erase(it);
            }
        }
    }
    std::string frame(Word pc) {
        char name[1024]{};
        Word offset = 0;
        std::ostringstream out;
        if (symbols->GetNameByOffset(pc, name, sizeof(name), nullptr, &offset) == S_OK) {
            out << name << "+0x" << std::hex << offset;
        } else {
            ULONG index;
            Word base;
            if (SUCCEEDED(symbols->GetModuleByOffset(pc, 0, &index, &base)) &&
                SUCCEEDED(symbols->GetModuleNameString(DEBUG_MODNAME_MODULE, index, base, name, sizeof(name), nullptr)))
                out << name << "+0x" << std::hex << (pc - base);
            else out << "0x" << std::hex << pc;
        }
        auto s = out.str();
        std::replace_if(s.begin(), s.end(), [](char c) { return c == '\t' || c == '\n' || c == '\r'; }, ' ');
        return s;
    }

    void enter(size_t index, ULONG thread, Word stack) {
        auto& f = functions[index];
        const auto start = Clock::now();
        const auto time = timestamp();
        const Word id = ++sequence;
        auto ret = read(stack, sizeof(void*));
        std::ostringstream line;
        line << "enter\t" << id << '\t' << index << '\t' << thread << '\t';
        const char* argRegs[] = {"rcx", "rdx", "r8", "r9"};
        Word slot = stack + sizeof(void*);
        for (size_t i = 0; i < f.widths.size(); ++i) {
            const auto bytes = f.widths[i] == L'8' ? 8 : 4;
            if (i) line << ',';
            auto value = f.widths[i] == L'?' ? std::nullopt :
                wide && i < 4 ? reg(argRegs[i]) : read(slot, wide ? 8 : bytes);
            line << number(value);
            slot += wide ? 8 : bytes;
        }
        line << '\t';
        DEBUG_STACK_FRAME frames[32]{};
        ULONG count = 0;
        control->GetStackTrace(0, 0, 0, frames, 32, &count);
        for (ULONG i = 0; i < count; ++i) {
            if (i) line << " | ";
            line << frame(frames[i].InstructionOffset);
        }
        if (!count) line << frame(f.base + f.rva) << " | " << (ret ? frame(*ret) : "?");
        std::cout << line.str() << '\t' << time << std::endl;
        if (!ret || !*ret) { std::cout << "incomplete\t" << id << std::endl; return; }
        point(*ret);
        calls.push_back({id, *ret, stack, thread, index, start});
    }

public:
    ComPtr<IDebugClient5> client;
    ComPtr<IDebugControl> control;
    std::vector<Function> functions;
    Capture() {
        check(DebugCreate(__uuidof(IDebugClient5), reinterpret_cast<void**>(client.GetAddressOf())), "DebugCreate");
        check(client.As(&control), "IDebugControl");
        check(client.As(&regs), "IDebugRegisters");
        check(client.As(&memory), "IDebugDataSpaces");
        check(client.As(&symbols), "IDebugSymbols");
        check(client.As(&system), "IDebugSystemObjects");
        check(symbols->SetSymbolPath("."), "Local symbols");
        check(control->AddEngineOptions(DEBUG_ENGOPT_INITIAL_BREAK | DEBUG_ENGOPT_FINAL_BREAK), "Engine options");
    }

    void bind() {
        ULONG loaded, unloaded;
        Word handle;
        check(symbols->GetNumberModules(&loaded, &unloaded), "List modules");
        check(system->GetCurrentProcessHandle(&handle), "Process handle");
        for (ULONG i = 0; i < loaded; ++i) {
            Word base;
            if (FAILED(symbols->GetModuleByIndex(i, &base))) continue;
            wchar_t path[32768];
            if (!GetModuleFileNameExW(reinterpret_cast<HANDLE>(static_cast<uintptr_t>(handle)),
                reinterpret_cast<HMODULE>(static_cast<uintptr_t>(base)), path, 32768)) {
                if (FAILED(symbols->GetModuleNameStringWide(DEBUG_MODNAME_IMAGE, i, base, path, 32768, nullptr))) continue;
            }
            for (auto& f : functions) {
                std::error_code ec;
                if (!f.base && std::filesystem::equivalent(path, f.image, ec)) {
                    point(base + f.rva);
                    f.base = base;
                }
            }
        }
    }

    void hit(ULONG thread) {
        Word pc, sp;
        check(regs->GetInstructionOffset(&pc), "Instruction pointer");
        check(regs->GetStackOffset(&sp), "Stack pointer");
        auto now = Clock::now();
        for (auto it = calls.rbegin(); it != calls.rend(); ++it) {
            if (it->thread != thread || it->address != pc || sp <= it->stack) continue;
            auto result = reg(wide ? "rax" : "eax");
            if (!wide && functions[it->function].result64) {
                auto high = reg("edx");
                if (result && high) *result |= *high << 32; else result.reset();
            }
            auto elapsed = std::chrono::duration_cast<std::chrono::microseconds>(now - it->entered).count();
            std::cout << "return\t" << it->id << '\t' << number(result) << '\t' << elapsed << std::endl;
            calls.erase(std::next(it).base());
            break;
        }
        for (size_t i = 0; i < functions.size(); ++i)
            if (functions[i].base && functions[i].base + functions[i].rva == pc) enter(i, thread, sp);
        prune();
    }

    void run(std::atomic_bool& stop) {
        bool ready = false;
        for (;;) {
            check(control->WaitForEvent(0, INFINITE), "WaitForEvent");
            if (stop) break;
            ULONG type, pid, tid;
            DEBUG_LAST_EVENT_INFO_EXCEPTION info{};
            check(control->GetLastEventInformation(&type, &pid, &tid, &info, sizeof(info), nullptr, nullptr, 0, nullptr), "Last event");
            ULONG thread = 0, mode = DEBUG_STATUS_GO;
            system->GetCurrentThreadSystemId(&thread);
            if (!ready) {
                check(client->AddProcessOptions(DEBUG_PROCESS_ONLY_THIS_PROCESS | DEBUG_PROCESS_DETACH_ON_EXIT), "Process options");
                check(control->Execute(DEBUG_OUTCTL_IGNORE, "sxe ld; sxe ud; sxe et; sxe epr", DEBUG_EXECUTE_NOT_LOGGED), "Event filters");
                bind();
                check(system->GetCurrentProcessSystemId(&pid), "Process ID");
                std::cout << "ready\t" << pid << '\t' << timestamp() << std::endl;
                ready = true;
            } else if (type == DEBUG_EVENT_EXIT_PROCESS) break;
            else if (type == DEBUG_EVENT_BREAKPOINT) hit(thread);
            else if (type == DEBUG_EVENT_LOAD_MODULE) bind();
            else if (type == DEBUG_EVENT_UNLOAD_MODULE) {
                Word base;
                memcpy(&base, &info, sizeof(base));
                for (auto& f : functions) if (f.base == base) f.base = 0;
                prune();
            } else if (type == DEBUG_EVENT_EXIT_THREAD) {
                calls.erase(std::remove_if(calls.begin(), calls.end(), [=](auto& call) {
                    if (call.thread != thread) return false;
                    std::cout << "incomplete\t" << call.id << std::endl;
                    return true;
                }), calls.end());
                prune();
            } else if (type == DEBUG_EVENT_EXCEPTION) mode = DEBUG_STATUS_GO_NOT_HANDLED;
            if (stop) break;
            check(control->SetExecutionStatus(mode), "Resume");
        }
    }
};

static void attach(IDebugClient5* client, ULONG pid) {
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!process) check(HRESULT_FROM_WIN32(GetLastError()), "Open target");
    HANDLE token;
    if (!OpenProcessToken(process, TOKEN_QUERY, &token)) check(HRESULT_FROM_WIN32(GetLastError()), "Target user");
    DWORD size = 0;
    GetTokenInformation(token, TokenUser, nullptr, 0, &size);
    std::vector<BYTE> owner(size);
    if (!GetTokenInformation(token, TokenUser, owner.data(), size, &size)) check(HRESULT_FROM_WIN32(GetLastError()), "Target user");
    CloseHandle(token);
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) check(HRESULT_FROM_WIN32(GetLastError()), "Current user");
    GetTokenInformation(token, TokenUser, nullptr, 0, &size);
    std::vector<BYTE> current(size);
    if (!GetTokenInformation(token, TokenUser, current.data(), size, &size)) check(HRESULT_FROM_WIN32(GetLastError()), "Current user");
    CloseHandle(token);
    if (!EqualSid(reinterpret_cast<TOKEN_USER*>(owner.data())->User.Sid, reinterpret_cast<TOKEN_USER*>(current.data())->User.Sid))
        throw std::runtime_error("Target must belong to the current user");
    auto hr = client->AttachProcess(0, pid, DEBUG_ATTACH_DEFAULT);
    CloseHandle(process);
    check(hr, "Attach target");
}

int wmain(int argc, wchar_t** argv) {
    if (argc < 7 || (argc - 3) % 4 != 0) return 2;
    try {
        Capture engine;
        for (int i = 3; i < argc; i += 4)
            engine.functions.push_back({argv[i], std::stoull(argv[i+1]), argv[i+2], std::wstring(argv[i+3]) == L"8"});
        if (std::wstring(argv[1]) == L"attach") attach(engine.client.Get(), std::stoul(argv[2]));
        else check(engine.client->CreateProcessWide(0, argv[2], DEBUG_ONLY_THIS_PROCESS | CREATE_NEW_CONSOLE), "Launch target");
        std::atomic_bool stop{false};
        try {
            std::thread([&] {
                std::string command;
                std::getline(std::cin, command);
                stop = true;
                engine.control->SetInterrupt(DEBUG_INTERRUPT_ACTIVE);
            }).detach();
            engine.run(stop);
        } catch (...) {
            engine.client->DetachProcesses();
            throw;
        }
        check(engine.client->DetachProcesses(), "Detach");
        std::cout << "stopped" << std::endl;
        ExitProcess(0);
    } catch (const std::exception& e) {
        std::cerr << e.what() << std::endl;
        ExitProcess(1);
    }
}
