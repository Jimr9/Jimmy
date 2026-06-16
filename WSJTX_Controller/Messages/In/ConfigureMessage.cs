using WsjtxUdpLib.Messages.Out;
using System;
using System.IO;

namespace WsjtxUdpLib.Messages
{
    /*
     * Configure      In       15                     quint32
     *                         Id (unique key)        utf8
     *                         Mode                   utf8
     *                         Frequency Tolerance    quint32
     *                         Submode                utf8
     *                         Fast Mode              bool
     *                         T/R Period             quint32
     *                         Rx DF                  quint32
     *                         DX Call                utf8
     *                         DX Grid                utf8
     *                         Generate Messages      bool
     *
     *      The server  may send  this message at  any time.   The message
     *      specifies  various  configuration  options.  For  utf8  string
     *      fields an empty value implies no change, for the quint32 Rx DF
     *      and  Frequency  Tolerance  fields the  maximum  quint32  value
     *      implies  no change.   Invalid or  unrecognized values  will be
     *      silently ignored.
     *
     *      Sending DX Call + Generate Messages = true is equivalent to the
     *      user typing a callsign into the DX Call box and clicking
     *      "Generate Std Msgs".  WSJT-X populates TX1-TX6 and selects TX1.
     */

    public class ConfigureMessage : WsjtxMessage, IWsjtxCommandMessageGenerator
    {
        public int SchemaVersion { get; set; }
        public string Id { get; set; }
        public string DXCall { get; set; }
        public string DXGrid { get; set; }
        public bool GenerateMessages { get; set; }

        public byte[] GetBytes()
        {
            using (MemoryStream m = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(m))
                {
                    writer.Write(WsjtxMessage.MagicNumber);
                    writer.Write(EncodeQUInt32((UInt32)SchemaVersion));
                    writer.Write(EncodeQUInt32((UInt32)MessageType.CONFIGURE_MESSAGE_TYPE));
                    writer.Write(EncodeString(Id));
                    writer.Write(EncodeString(""));                  // Mode: no change
                    writer.Write(EncodeQUInt32(UInt32.MaxValue));    // FrequencyTolerance: no change
                    writer.Write(EncodeString(""));                  // Submode: no change
                    writer.Write(EncodeBoolean(false));              // FastMode: FT8 default
                    writer.Write(EncodeQUInt32(UInt32.MaxValue));    // TRPeriod: no change
                    writer.Write(EncodeQUInt32(UInt32.MaxValue));    // RxDF: no change
                    writer.Write(EncodeString(DXCall ?? ""));
                    writer.Write(EncodeString(DXGrid ?? ""));
                    writer.Write(EncodeBoolean(GenerateMessages));
                }
                return m.ToArray();
            }
        }
    }
}
