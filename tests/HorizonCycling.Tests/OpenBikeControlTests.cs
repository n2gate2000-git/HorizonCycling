using System;
using System.Collections.Generic;
using System.Linq;
using HorizonCyclingBridge.Controller;
using Xunit;

namespace HorizonCycling.Tests
{
    public class OpenBikeControlTests
    {
        // テスト用バイト列は OpenBikeControl プロトコルリポジトリの examples/python/protocol_parser.py (encode_button_state / encode_app_info) で生成

        [Fact]
        public void Mapper_SinglePressAndRelease_PressesAndReleasesButton()
        {
            var mapper = new OpenBikeControlMapper();

            var press = mapper.ProcessMessage(new byte[] { 0x01, 0x01, 0x01 });
            Assert.Single(press);
            Assert.Equal("Button2", press[0].Target);
            Assert.True(press[0].Pressed);

            var release = mapper.ProcessMessage(new byte[] { 0x01, 0x01, 0x00 });
            Assert.Single(release);
            Assert.Equal("Button2", release[0].Target);
            Assert.False(release[0].Pressed);
        }

        [Fact]
        public void Mapper_ShiftUpDifficultyAndSelectInOneMessage_TriggersOnlyShiftUp()
        {
            var mapper = new OpenBikeControlMapper();

            // Shift Up + Increase Difficulty + Select (1つの物理ボタン)
            var press = mapper.ProcessMessage(new byte[] { 0x01, 0x01, 0x01, 0x30, 0x01, 0x14, 0x01 });
            Assert.Single(press);
            Assert.Equal("Button2", press[0].Target);
            Assert.True(press[0].Pressed);

            var release = mapper.ProcessMessage(new byte[] { 0x01, 0x01, 0x00, 0x30, 0x00, 0x14, 0x00 });
            Assert.Single(release);
            Assert.Equal("Button2", release[0].Target);
            Assert.False(release[0].Pressed);
        }

        [Fact]
        public void Mapper_DifficultyAndSelect_TriggersDifficulty()
        {
            var mapper = new OpenBikeControlMapper();

            var press = mapper.ProcessMessage(new byte[] { 0x01, 0x31, 0x01, 0x14, 0x01 });
            Assert.Single(press);
            Assert.Equal("DifficultyDown", press[0].Target);
        }

        [Fact]
        public void Mapper_BackAndBrake_TriggersBrakeWithAnalogStrength()
        {
            var mapper = new OpenBikeControlMapper();

            // Back + Brake 50% (0x33 = 50 + 1)
            var press = mapper.ProcessMessage(new byte[] { 0x01, 0x15, 0x01, 0x1A, 0x33 });
            Assert.Single(press);
            Assert.Equal("Brake", press[0].Target);
            Assert.Equal(0.50f, press[0].Value, 3);
        }

        [Fact]
        public void Mapper_BrakeStrengthChange_UpdatesBrakeAndClampsAbove100()
        {
            var mapper = new OpenBikeControlMapper();

            var press = mapper.ProcessMessage(new byte[] { 0x01, 0x1A, 0x65 }); // 100%
            Assert.Single(press);
            Assert.Equal(1.0f, press[0].Value, 3);

            var update = mapper.ProcessMessage(new byte[] { 0x01, 0x1A, 0x97 }); // 150% -> 100%
            Assert.Single(update);
            Assert.True(update[0].Pressed);
            Assert.Equal(1.0f, update[0].Value, 3);

            var lighter = mapper.ProcessMessage(new byte[] { 0x01, 0x1A, 0x15 }); // 20%
            Assert.Single(lighter);
            Assert.Equal(0.20f, lighter[0].Value, 3);

            var release = mapper.ProcessMessage(new byte[] { 0x01, 0x1A, 0x00 });
            Assert.Single(release);
            Assert.False(release[0].Pressed);
        }

        [Fact]
        public void Mapper_SeparateMessages_AreSeparatePresses()
        {
            var mapper = new OpenBikeControlMapper();

            var first = mapper.ProcessMessage(new byte[] { 0x01, 0x1A, 0x01 });
            Assert.Equal("Brake", Assert.Single(first).Target);

            // ブレーキを握ったままシフトダウン
            var second = mapper.ProcessMessage(new byte[] { 0x01, 0x02, 0x01 });
            Assert.Equal("Button3", Assert.Single(second).Target);

            var releaseShift = mapper.ProcessMessage(new byte[] { 0x01, 0x02, 0x00 });
            Assert.Equal("Button3", Assert.Single(releaseShift).Target);

            var releaseBrake = mapper.ProcessMessage(new byte[] { 0x01, 0x1A, 0x00 });
            Assert.Equal("Brake", Assert.Single(releaseBrake).Target);
        }

        [Fact]
        public void Mapper_UnmappedAndAnalogOnlyButtons_AreIgnored()
        {
            var mapper = new OpenBikeControlMapper();

            // Steering Angle (0x1B) + Steer Right (0x19): ステアリングは未割り当て
            Assert.Empty(mapper.ProcessMessage(new byte[] { 0x01, 0x1B, 0x94, 0x19, 0x01 }));
            Assert.Empty(mapper.ProcessMessage(new byte[] { 0x01, 0x1B, 0x80, 0x19, 0x00 }));

            // 不正なメッセージ種別・短すぎるメッセージ
            Assert.Empty(mapper.ProcessMessage(new byte[] { 0x02, 0x01, 0x01 }));
            Assert.Empty(mapper.ProcessMessage(new byte[] { 0x01 }));
        }

        [Fact]
        public void Mapper_UnmappedHigherPriority_FallsBackToMappedButton()
        {
            var map = OpenBikeControlMapper.CreateDefaultMap();
            map["ShiftUp"] = "None";
            var mapper = new OpenBikeControlMapper(map);

            var press = mapper.ProcessMessage(new byte[] { 0x01, 0x01, 0x01, 0x30, 0x01, 0x14, 0x01 });
            Assert.Equal("DifficultyUp", Assert.Single(press).Target);
            Assert.DoesNotContain((byte)0x01, mapper.SupportedButtonIds);
        }

        [Fact]
        public void Mapper_Reset_ReleasesActivePresses()
        {
            var mapper = new OpenBikeControlMapper();
            mapper.ProcessMessage(new byte[] { 0x01, 0x1A, 0x01 });
            mapper.ProcessMessage(new byte[] { 0x01, 0x14, 0x01 });

            var released = mapper.Reset();
            Assert.Equal(2, released.Count);
            Assert.All(released, a => Assert.False(a.Pressed));

            // 解放後は同じボタンを再び押下として扱う
            Assert.Single(mapper.ProcessMessage(new byte[] { 0x01, 0x14, 0x01 }));
        }

        [Fact]
        public void BuildAppInfo_MatchesProtocolReferenceEncoding()
        {
            byte[] expected =
            {
                0x04, 0x01, 0x0E, 0x68, 0x6F, 0x72, 0x69, 0x7A, 0x6F, 0x6E, 0x63, 0x79, 0x63, 0x6C, 0x69, 0x6E, 0x67,
                0x05, 0x31, 0x2E, 0x34, 0x2E, 0x30, 0x03, 0x01, 0x02, 0x10
            };

            byte[] actual = OpenBikeControlMapper.BuildAppInfo("horizoncycling", "1.4.0", new List<byte> { 0x01, 0x02, 0x10 });
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void SupportedButtonIds_DefaultMap_ContainsMappedIdsOnly()
        {
            var mapper = new OpenBikeControlMapper();
            var ids = mapper.SupportedButtonIds;

            Assert.Contains((byte)0x01, ids);
            Assert.Contains((byte)0x1A, ids);
            Assert.Contains((byte)0x30, ids);
            Assert.Contains((byte)0x14, ids);
            Assert.DoesNotContain((byte)0x1B, ids);
            Assert.Equal(ids.OrderBy(i => i), ids);
        }
    }
}
