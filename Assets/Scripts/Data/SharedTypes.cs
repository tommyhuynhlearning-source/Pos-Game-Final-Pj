using System;
using UnityEngine;
using POSTechSupport.Core;

namespace POSTechSupport.Data
{
    // ============================================================================
    // Serializable value types shared by the ScriptableObjects (Docs/schema.md §5)
    // and the simulation layer. All state values are stored as strings so IssueSO /
    // DesktopActionSO stay fully data-driven (FaultInjection.faultValue and
    // StateCheck.expectedValue are strings in the schema) — booleans are "true"/"false".
    // ============================================================================

    /// <summary>TẦNG 1 — a single state mutation to apply to a module (inject a fault, or a Fix's write).</summary>
    [Serializable]
    public class FaultInjection
    {
        public ModuleType module;
        public string stateField;
        public string faultValue;
    }

    /// <summary>TẦNG 2 — layman is all the customer ever says; technical only shows on remote.</summary>
    [Serializable]
    public class Symptom
    {
        [TextArea] public string layman;
        [TextArea] public string technical;
    }

    /// <summary>TẦNG 3 — a clue surfaced by a diagnostic action; red herrings mislead.</summary>
    [Serializable]
    public class DiagnosticClue
    {
        public DesktopActionType revealedBy;
        [TextArea] public string clueText;
        public bool isRedHerring;
    }

    /// <summary>TẦNG 4 — what "fixed" means: root cause states + optional test pass.</summary>
    [Serializable]
    public class ResolutionCondition
    {
        public StateCheck[] symptomCleared;   // temp fix: symptom gone, customer happy
        public StateCheck[] rootCauseFixed;   // real fix: root cause healthy
        public bool requiresTestPass;
        public ReceiptType testReceiptType;
    }

    /// <summary>A data-driven comparison of one module field against an expected value.</summary>
    [Serializable]
    public class StateCheck
    {
        public ModuleType module;
        public string field;
        public ComparisonOp op;
        public string expectedValue;
    }

    /// <summary>PersonaProfileSO misnaming entry — "POS software" → "the till".</summary>
    [Serializable]
    public class MisnameEntry
    {
        public string correctTerm;
        public string customerTerm;
    }

    /// <summary>StoreProfileSO machine — a register with its healthy baseline state.</summary>
    [Serializable]
    public class MachineConfig
    {
        public string machineId;
        public string osVersion;
        public string posSoftwareVersion;
        public HardwareSpec hardware;
        public ModuleBaseline baseline;   // cloned at runtime into a VirtualDesktopInstance
    }

    [Serializable]
    public class HardwareSpec
    {
        public string cpu;
        public string ram;
        public string notes;
    }

    /// <summary>
    /// The healthy default state of every module, cloned when a desktop is built.
    /// Mirrors the prototype's freshDesktop(). Values are strings ("true"/"false" for bools).
    /// </summary>
    [Serializable]
    public class ModuleBaseline
    {
        // OS (Windows) — machine-wide faults block everything; service faults surface downstream
        public string osDiskSpace = "OK";
        public string osPendingReboot = "false";
        public string osSpoolerService = "Running";
        public string osSystemTime = "OK";
        public string osAntivirusQuarantine = "false";
        public string osUserAccount = "Admin";
        public string osPowerPlan = "AlwaysOn";

        // Network — isOnline is "down"; the rest are "up but degraded", which cascade differently
        public string networkIsOnline = "true";
        public string networkSsid = "SunriseDiner-Main";
        public string networkSignalStrength = "Good";
        public string networkDnsServer = "192.168.1.1";
        public string networkFirewallBlocking = "false";

        // POSSoftware
        public string posReceiptTemplate = "OK";
        public string posStaffRole = "Sale";      // floor staff; Admin means refund/void/settle rights
        public string posStaffTerminal = "REG-1";
        public string posTerminalSynced = "true";
        public string posDbHost = "db.sunrisediner.local";
        public string posRegisteredTerminalIp = "192.168.1.50";
        public string posLicenseState = "Valid";
        public string posOfflineMode = "false";
        public string posBatchState = "Open";
        public string posTaxRate = "8.25";
        public string posPriceSync = "Current";
        public string posPrinterVisible = "true";
        public string posMinTerminalFirmware = "4.0";

        // Terminal (wifiNetwork is the only editable field; IP/gateway derive from it)
        public string terminalWifiNetwork = "SunriseDiner-Main";
        public string terminalMachineId = "REG-1";   // which register this is — staff assignments point at it
        public string terminalPairingState = "Paired";
        public string terminalFirmwareVersion = "4.2";
        public string terminalEmvConfig = "OK";
        public string terminalMode = "Live";

        // Printer
        public string printerPaperLevel = "OK";
        public string printerDriverState = "OK";
        public string printerConnection = "Connected";
        public string printerPort = "COM3";
        public string printerCableConnected = "true";
        public string printerPaperJam = "None";
        public string printerQueuePaused = "false";
        public string printerDefaultPrinter = "ReceiptPrinter";
        public string printerPaperWidth = "80mm";

        // CashDrawer
        public string cashDrawerPort = "COM4";
        public string cashDrawerLockState = "Unlocked";
        public string cashDrawerTriggerMode = "OnPrint";
    }

    /// <summary>ReceiptTemplateSO field descriptor.</summary>
    [Serializable]
    public class ReceiptField
    {
        public string label;
        public bool required;
    }

    /// <summary>
    /// Result of a module's EffectiveStatus: the status plus a human-readable reason that
    /// points back upstream (the breadcrumb the player follows). Not serialized — runtime only.
    /// </summary>
    public readonly struct StatusResult
    {
        public readonly Status status;
        public readonly string reason;
        public StatusResult(Status status, string reason = "")
        {
            this.status = status;
            this.reason = reason;
        }
        public bool IsOk => status == Status.OK;
    }
}
