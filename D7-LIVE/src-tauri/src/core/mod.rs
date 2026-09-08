#[derive(Debug,Clone,Copy,PartialEq,Eq)]
pub enum CapabilityState { Installed, Running, Unavailable, RepairRequired }

pub mod capture {
    #[derive(Debug,Clone,Copy,PartialEq,Eq)] pub enum Backend { GameHook, WindowsGraphicsCapture, WindowCapture }
}

pub mod encoding {
    #[derive(Debug,Clone,Copy,PartialEq,Eq)] pub enum Encoder { NvencH264, NvencHevc, SoftwareH264 }
}
