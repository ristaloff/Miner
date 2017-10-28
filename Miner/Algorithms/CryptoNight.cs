﻿using System;
using System.Diagnostics;
using DotNetStratumMiner;
using Org.BouncyCastle.Crypto.Engines;
using BigMath;
using Newtonsoft.Json;
using CryptoHash;

namespace HD
{
  /// <summary>
  /// I believe this is the only information which differs per thread.
  /// 
  /// TODO perf: reuse the arrays
  /// </summary>
  public class cryptonight_ctx
  {
    public readonly byte[] keccakHash = new byte[200]; 
    public readonly byte[] scratchpad = new byte[2097152]; 
  }

  public class SomethingSomethingWIPResultStuff
  {
    public byte[] bResult;
    public string sJobID;
    public uint iNonce;

    public SomethingSomethingWIPResultStuff(
      string sJobID)
    {
      this.sJobID = sJobID;
    }
  }

  public class CryptoNight
  {
    const int iThreadCount = 1;
    const int iThreadNo = 0;
    const int iResumeCnt = 0; // What's this for?

    public byte[] bWorkBlob = new byte[112];
    public SomethingSomethingWIPResultStuff result;
    public cryptonight_ctx ctx;

    public int iWorkSize;
    public ulong iJobDiff;
    public ulong iTarget;
    public uint iCount;

    public byte[] a;
    public byte[] b;

    public byte[][] blocks;
    byte[] key;

    Job newJob;

    public AesEngine aes;

    public ulong piHashVal
    {
      get
      {
        return BitConverter.ToUInt64(result.bResult, 24);
      }
    }
    public uint piNonce
    {
      get
      {
        return BitConverter.ToUInt32(bWorkBlob, 39);
      }
      set
      {
        byte[] data = BitConverter.GetBytes(value);
        bWorkBlob[39] = data[0];
        bWorkBlob[40] = data[1];
        bWorkBlob[41] = data[2];
        bWorkBlob[42] = data[3];
      }
    }

    public void Process(Job newJob, string nonceOverride = null)
    {
      this.newJob = newJob;
      byte[] databyte = Utilities.HexStringToByteArray(newJob.Blob);
      iWorkSize = databyte.Length;
      byte[] targetbyte = Utilities.HexStringToByteArray(newJob.Target);
      ctx = new cryptonight_ctx();


      // 76 byte array
      //byte[] blob = StringToByteArray(newJob.Result.Job.Blob);
      for (int i = 0; i < databyte.Length; i++)
      {
        bWorkBlob[i] = databyte[i];
      }



      iTarget = t32_to_t64(hex2bin(newJob.Target, 8));
      iJobDiff = t64_to_diff(iTarget);

      iCount = 0;
      result = new SomethingSomethingWIPResultStuff(
        newJob.Job_Id);
      result.iNonce = calc_nicehash_nonce(piNonce, iResumeCnt);
      if(nonceOverride != null)
      {
        result.iNonce = uint.Parse(nonceOverride, System.Globalization.NumberStyles.HexNumber);
      }
    }

    public void ProcessStep2()
    {
      piNonce = ++result.iNonce;
    }

    public void ProcessStep3()
    {
      KeccakDigest.keccak(bWorkBlob, iWorkSize, ctx.keccakHash, 200);
    }

    public string GetResultJson()
    {
      // TODO I think the Id is the original request ID...
      Algorithms.NiceHashResultJson json = new Algorithms.NiceHashResultJson(1, newJob.Job_Id, result.iNonce, result.bResult);

      return JsonConvert.SerializeObject(json);
    }

    public void ProcessStep4()
    {
      key = new byte[32];
      for (int i = 0; i < key.Length; i++)
      {
        key[i] = ctx.keccakHash[i];
      }


      aes = new AesEngine();
      aes.Init(key);

      // TODO unit test for the key before we go on.

    }

    public void ProcessStep5()
    {
      ExtractBlocksFromHash();
    }

    private void ExtractBlocksFromHash()
    {
      blocks = new byte[8][]; // 8 blocks
      for (int blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
      {
        blocks[blockIndex] = new byte[16]; // 16 bytes per block
        for (int byteIndex = 0; byteIndex < 16; byteIndex++)
        {
          blocks[blockIndex][byteIndex] = ctx.keccakHash[64 + blockIndex * 16 + byteIndex];
        }
      }
    }

    /// <summary>
    /// One AES round per block
    /// </summary>
    public void ProcessStep6()
    {
      EncryptBlocks();
    }

    private void EncryptBlocks()
    {
      for (int blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
      {
        aes.ProcessBlock(blocks[blockIndex], 0, blocks[blockIndex], 0);
      }
    }

    /// <summary>
    /// Copy blocks into the scratchpad
    /// </summary>
    public void ProcessStep7()
    {
      for (int blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
      {
        byte[] block = blocks[blockIndex];
        for (int byteIndex = 0; byteIndex < block.Length; byteIndex++)
        {
          ctx.scratchpad[blockIndex * block.Length + byteIndex] = block[byteIndex];
        }
      }
    }

    /// <summary>
    /// Complete the scratchpad
    /// </summary>
    public void ProcessStep8()
    {
      for (int scratchIndex = 1; scratchIndex < 16384; scratchIndex++)
      {
        for (int blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
          aes.ProcessBlock(blocks[blockIndex], 0, blocks[blockIndex], 0);
        }

        for (int blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
          byte[] block = blocks[blockIndex];
          for (int byteIndex = 0; byteIndex < block.Length; byteIndex++)
          {
            ctx.scratchpad[scratchIndex * blocks.Length * block.Length + blockIndex * block.Length + byteIndex] = block[byteIndex];
          }
        }
      }
    }

    public void ProcessStep9()
    {
      a = new byte[16];
      b = new byte[16];
      for (int i = 0; i < key.Length; i++)
      {
        if (i < 16)
        {
          a[i] = (byte)(key[i] ^ ctx.keccakHash[32 + i]);
        }
        else
        {
          b[i - 16] = (byte)(key[i] ^ ctx.keccakHash[32 + i]);
        }
      }

    }

    public void ProcessStep10()
    {
      ulong idx0 = BitConverter.ToUInt64(a, 0);

      // Optim - 90% time boundary
      for (int i = 0; i < 524288; i++)
      {
        // cx = scratchpad[scratchpad_address];
        byte[] cx = new byte[16];
        for (int tempIndex = 0; tempIndex < cx.Length; tempIndex++)
        {
          cx[tempIndex] = ctx.scratchpad[(int)(idx0 & 0x1FFFF0) + tempIndex];
        }

        // scratchpad_address = to_scratchpad_address(a)
        // scratchpad[scratchpad_address] = aes_round(scratchpad[scratchpad_address], a)

        uint[] key = new uint[4];
        for (int keyIndex = 0; keyIndex < key.Length; keyIndex++)
        {
          key[keyIndex] = BitConverter.ToUInt32(a, keyIndex * 4);
        }

        aes.DoRounds(cx, 0, key, cx, 0);

        // scratchpad[scratchpad_address] = b xor scratchpad[scratchpad_address]
        for (int bIndex = 0; bIndex < 16; bIndex++)
        {
          ctx.scratchpad[(int)(idx0 & 0x1FFFF0) + bIndex] = (byte)(cx[bIndex] ^ b[bIndex]);
        }

        idx0 = BitConverter.ToUInt64(cx, 0);

        // b = temp;
        for (int bIndex = 0; bIndex < 16; bIndex++)
        {
          b[bIndex] = cx[bIndex];
        }

        // scratchpad_address = to_scratchpad_address(b)
        // a = 8byte_add(a, 8byte_mul(b, scratchpad[scratchpad_address]))
        ulong hi, lo, cl, ch;
        cl = BitConverter.ToUInt64(ctx.scratchpad, (int)(idx0 & 0x1FFFF0));
        ch = BitConverter.ToUInt64(ctx.scratchpad, (int)(idx0 & 0x1FFFF0) + sizeof(ulong));

        Int128 mul = new Int128(0, idx0) * new Int128(0, cl);
        Int128 aInt = new Int128(BitConverter.ToUInt64(a, 8), BitConverter.ToUInt64(a, 0));

        unchecked
        {
          aInt = new Int128(mul.Low + aInt.High, mul.High + aInt.Low);
        }

        byte[] mulHigh = BitConverter.GetBytes(aInt.High);
        byte[] mulLow = BitConverter.GetBytes(aInt.Low);

        for (int aIndex = 0; aIndex < 16; aIndex++)
        {
          if (aIndex >= 8)
          {
            a[aIndex] = mulHigh[aIndex - 8];
          }
          else
          {
            a[aIndex] = mulLow[aIndex];
          }
        }

        // temp = a;
        // a = a xor scratchpad[scratchpad_address]
        // scratchpad[scratchpad_address] = temp

        if (ctx.scratchpad[0] != 248)
        {
          Console.WriteLine();
        }

        byte[] temp = new byte[16];
        for (int aIndex = 0; aIndex < 16; aIndex++)
        {
          temp[aIndex] = ctx.scratchpad[(int)(idx0 & 0x1FFFF0) + aIndex];
        }

        for (int aIndex = 0; aIndex < 16; aIndex++)
        {
          ctx.scratchpad[(int)(idx0 & 0x1FFFF0) + aIndex] = a[aIndex];
          a[aIndex] ^= temp[aIndex];
        }

        // cache a for next loop
        idx0 = BitConverter.ToUInt64(a, 0);
      }



      Console.WriteLine();
    }

    public void ProcessStep11()
    {
      // Should we not be sharing this key?
      key = new byte[32];
      for (int i = 0; i < key.Length; i++)
      {
        key[i] = ctx.keccakHash[i + 32];
      }

      aes = new AesEngine();
      aes.Init(key);
    }

    public void ProcessStep12()
    {
      ExtractBlocksFromHash();

      //for (int byteIndex = 0; byteIndex < 128; byteIndex++)
      //{
      //  byte hashValue = ctx.hash_state[byteIndex + 64];
      //  byte scratchValue = ctx.long_state[byteIndex];
      //  ctx.long_state[byteIndex] = (byte)(hashValue ^ scratchValue);
      //}

      for (int scratchIndex = 0; scratchIndex < 16384; scratchIndex++)
      {
        for (int blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
          for (int byteIndex = 0; byteIndex < 16; byteIndex++)
          {
            blocks[blockIndex][byteIndex] ^=
              ctx.scratchpad[byteIndex + scratchIndex * 128 + blockIndex * blocks[blockIndex].Length];
          }
        }

        EncryptBlocks();
      }


      for (int blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
      {
        for (int byteIndex = 0; byteIndex < 16; byteIndex++)
        {
          ctx.keccakHash[64 + byteIndex + blockIndex * 16] = blocks[blockIndex][byteIndex];
        }
      }

    }

    public void ProcessStep13()
    {
      ulong[] tempLong = new ulong[200 / sizeof(ulong) / sizeof(byte)];
      for (int i = 0; i < tempLong.Length; i++)
      {
        tempLong[i] = BitConverter.ToUInt64(ctx.keccakHash, i * sizeof(ulong) / sizeof(byte));
      }

      KeccakDigest.keccakf(tempLong, KeccakDigest.KECCAK_ROUNDS);

      for (int longIndex = 0; longIndex < tempLong.Length; longIndex++)
      {
        byte[] longData = BitConverter.GetBytes(tempLong[longIndex]);
        for (int byteIndex = 0; byteIndex < 8; byteIndex++)
        {
          ctx.keccakHash[longIndex * sizeof(ulong) + byteIndex] = longData[byteIndex];
        }
      }
    }

    uint hex2bin(string input, uint len)
    {
      bool error = false;
      byte[] output = new byte[len / 2];
      for (int i = 0; i < len; i += 2)
      {
        output[i / 2] = (byte)((hf_hex2bin((byte)input[i], ref error) << 4) | hf_hex2bin((byte)input[i + 1], ref error));
        if (error)
        {
          throw new Exception(); // error handling...
        }
      }
      return BitConverter.ToUInt32(output, 0);
    }

    public int hashID;

    public void ProcessStep14()
    {
      hashID = ctx.keccakHash[0] & 0b0011;
    }
    public void ProcessStep15()
    {
      Digest hash;
      switch (hashID)
      {
        case 0:
          {
            hash = new BLAKE256();
            break;
          }
        case 1:
          {
            hash = new Groestl256();
            break;
          }
        case 2:
          {
            hash = new JH256();
            break;
          }
        case 3:
          {
            hash = new Skein256();
            break;
          }
        default:
          Debug.Fail("Missing hash?!");
          hash = null;
          break;
      }

      hash.update(ctx.keccakHash);
      result.bResult = hash.digest();
    }

    byte hf_hex2bin(byte c, ref bool err)
    {
      if (c >= '0' && c <= '9')
      {
        return (byte)(c - '0');
      }
      else if (c >= 'a' && c <= 'f')
      {
        return (byte)(c - 'a' + 0xA);
      }
      else if (c >= 'A' && c <= 'F')
      {
        return (byte)(c - 'A' + 0xA);
      }

      err = true;
      return 0;
    }

    uint calc_nicehash_nonce(
          uint start,
          uint resume)
    {
      return start | (resume * iThreadCount + iThreadNo) << 18;
    }

    static ulong t64_to_diff(ulong t)
    {
      return 0xFFFFFFFFFFFFFFFF / t;
    }

    public static byte[] StringToByteArray(String hex)
    {
      int NumberChars = hex.Length;
      byte[] bytes = new byte[NumberChars / 2];
      for (int i = 0; i < NumberChars; i += 2)
        bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
      return bytes;
    }

    static ulong t32_to_t64(uint t)
    {
      return 0xFFFFFFFFFFFFFFFF / (0xFFFFFFFF / ((ulong)t));
    }
  }

}