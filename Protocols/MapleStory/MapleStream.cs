using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MultiShark.Protocols.MapleStory
{
    [Flags]
    public enum TransformMethod : int
    {
        AES = 1 << 1,
        MAPLE_CRYPTO = 1 << 2,
        OLD_KMS_CRYPTO = 1 << 3,
        KMS_CRYPTO = 1 << 4,

        SHIFT_IV = 1 << 5,
        SHIFT_IV_OLD = 1 << 6,
        NONE = 0
    }

    sealed class MapleStream : BaseStream<MaplePacket, MapleOpcode>
    {
        private bool _outbound = false;
        private MapleAES _aes = null;
        private ushort _build = 0;
        private byte _locale = 0;

        private TransformMethod _transformMethod;
        private bool _usesByteHeader = false;

        public MapleStream(bool pOutbound, ushort pBuild, byte pLocale, byte[] pIV, byte pSubVersion)
        {
            _outbound = pOutbound;
            _locale = pLocale;
            _build = pBuild;
            if (pOutbound)
                _aes = new MapleAES(pBuild, pLocale, pIV, pSubVersion);
            else
                _aes = new MapleAES((ushort)(0xFFFF - pBuild), pLocale, pIV, pSubVersion);

            if ((pLocale == MapleLocale.TESPIA && pBuild == 40) ||
                (pLocale == MapleLocale.SOUTH_EAST_ASIA && pBuild == 15))
            {
                // WvsBeta
                _transformMethod = TransformMethod.MAPLE_CRYPTO | TransformMethod.SHIFT_IV;
                _usesByteHeader = true;
            }
            else if (pLocale == MapleLocale.KOREA_TEST && pBuild == 255)
            {
                // KMSB (Modified client)
                _transformMethod = TransformMethod.OLD_KMS_CRYPTO | TransformMethod.SHIFT_IV_OLD;
                _usesByteHeader = true;
            }
            else if (
                pLocale == MapleLocale.TAIWAN ||
                pLocale == MapleLocale.CHINA ||
                pLocale == MapleLocale.TESPIA ||
                pLocale == MapleLocale.JAPAN ||
                (pLocale == MapleLocale.GLOBAL && (short)pBuild >= 149) ||
                (pLocale == MapleLocale.KOREA && pBuild >= 221) ||
                (pLocale == MapleLocale.SOUTH_EAST_ASIA && pBuild >= 144))
            {
                // TWMS / CMS / CMST / JMS / GMS (>= 149)
                _transformMethod = TransformMethod.AES | TransformMethod.SHIFT_IV;
            }
            else if (pLocale == MapleLocale.KOREA || pLocale == MapleLocale.KOREA_TEST)
            {
                // KMS / KMST
                _transformMethod = TransformMethod.KMS_CRYPTO;
            }
            else
            {
                // All others lol
                _transformMethod = TransformMethod.AES | TransformMethod.MAPLE_CRYPTO | TransformMethod.SHIFT_IV;
            }

            Console.WriteLine("Using transform methods: {0}", _transformMethod);
        }

        public override MaplePacket Read(DateTime pTransmitted)
        {
            if (_cursor < 4) return null;
            if (!_aes.ConfirmHeader(_buffer, 0))
            {
                throw new Exception("Failed to confirm packet header");
            }

            ushort packetSize = _aes.GetHeaderLength(_buffer, 0, _build == 255 && _locale == 1);
            if (_cursor < (packetSize + 4))
                return null;
            byte[] packetBuffer = new byte[packetSize];
            Buffer.BlockCopy(_buffer, 4, packetBuffer, 0, packetSize);

            var preDecodeIV = BitConverter.ToUInt32(_aes.mIV, 0);

            Decrypt(packetBuffer, _build, _locale, _transformMethod);

            var postDecodeIV = BitConverter.ToUInt32(_aes.mIV, 0);

            _cursor -= (packetSize + 4);
            if (_cursor > 0) Buffer.BlockCopy(_buffer, packetSize + 4, _buffer, 0, _cursor);
            ushort opcode;

            if (_usesByteHeader)
            {
                opcode = (ushort)(packetBuffer[0]);
                Buffer.BlockCopy(packetBuffer, 1, packetBuffer, 0, packetSize - 1);
                Array.Resize(ref packetBuffer, packetSize - 1);
            }
            else
            {
                opcode = (ushort)(packetBuffer[0] | (packetBuffer[1] << 8));
                Buffer.BlockCopy(packetBuffer, 2, packetBuffer, 0, packetSize - 2);
                Array.Resize(ref packetBuffer, packetSize - 2);
            }

            Definition definition = Config.Instance.GetDefinition(_build, _locale, _outbound, opcode);
            return new MaplePacket(pTransmitted, _outbound, _build, _locale, opcode, definition == null ? "" : definition.Name, packetBuffer, preDecodeIV, postDecodeIV);
        }

        private void Decrypt(byte[] pBuffer, ushort pBuild, byte pLocale, TransformMethod pTransformLocale)
        {
            if ((pTransformLocale & TransformMethod.AES) != 0) _aes.TransformAES(pBuffer);

            if ((pTransformLocale & TransformMethod.MAPLE_CRYPTO) != 0)
            {
                for (int index1 = 1; index1 <= 6; ++index1)
                {
                    byte firstFeedback = 0;
                    byte secondFeedback = 0;
                    byte length = (byte)(pBuffer.Length & 0xFF);
                    if ((index1 % 2) == 0)
                    {
                        for (int index2 = 0; index2 < pBuffer.Length; ++index2)
                        {
                            byte temp = pBuffer[index2];
                            temp -= 0x48;
                            temp = (byte)(~temp);
                            temp = RollLeft(temp, length & 0xFF);
                            secondFeedback = temp;
                            temp ^= firstFeedback;
                            firstFeedback = secondFeedback;
                            temp -= length;
                            temp = RollRight(temp, 3);
                            pBuffer[index2] = temp;
                            --length;
                        }
                    }
                    else
                    {
                        for (int index2 = pBuffer.Length - 1; index2 >= 0; --index2)
                        {
                            byte temp = pBuffer[index2];
                            temp = RollLeft(temp, 3);
                            temp ^= 0x13;
                            secondFeedback = temp;
                            temp ^= firstFeedback;
                            firstFeedback = secondFeedback;
                            temp -= length;
                            temp = RollRight(temp, 4);
                            pBuffer[index2] = temp;
                            --length;
                        }
                    }
                }
            }

            if ((pTransformLocale & TransformMethod.KMS_CRYPTO) != 0) _aes.TransformKMS(pBuffer);
            if ((pTransformLocale & TransformMethod.OLD_KMS_CRYPTO) != 0) _aes.TransformOldKMS(pBuffer);

            if ((pTransformLocale & TransformMethod.SHIFT_IV) != 0) _aes.ShiftIV();
            if ((pTransformLocale & TransformMethod.SHIFT_IV_OLD) != 0) _aes.ShiftIVOld();
        }

        public static byte RollLeft(byte pThis, int pCount)
        {
            uint overflow = ((uint)pThis) << (pCount % 8);
            return (byte)((overflow & 0xFF) | (overflow >> 8));
        }

        public static byte RollRight(byte pThis, int pCount)
        {
            uint overflow = (((uint)pThis) << 8) >> (pCount % 8);
            return (byte)((overflow & 0xFF) | (overflow >> 8));
        }
    }
}
