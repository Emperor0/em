use serde::Serialize;
#[derive(Debug,Clone,Copy,Serialize)] pub enum DeviceState {Installed,Running,Unavailable,RepairRequired}
#[derive(Debug,Clone,Copy,Serialize)] pub struct VirtualOutputState {pub camera:DeviceState,pub audio:DeviceState}
impl Default for VirtualOutputState{fn default()->Self{Self{camera:DeviceState::Unavailable,audio:DeviceState::Unavailable}}}
