//
// This is free and unencumbered software released into the public domain.
//
// Anyone is free to copy, modify, publish, use, compile, sell, or
// distribute this software, either in source code form or as a compiled
// binary, for any purpose, commercial or non-commercial, and by any
// means.
//
// For more information, please refer to <https://unlicense.org>
//

#include "CppUTest/TestHarness.h"

TEST_GROUP(MyFunnyValentine_testGroup)
{
};

TEST_GROUP(AquaManAndBarnacleBoy_testGroup)
{
};

//--------------------------------------------------------------------------------------------------
TEST(MyFunnyValentine_testGroup, test_FailingTest1)
{
  const char* a = "steve";
  STRCMP_EQUAL(a, "harvey");
}

TEST(MyFunnyValentine_testGroup, test_PassingTest2)
{
  const char* a = "steve";
  char b[] = { 's', 't', 'e', 'v', 'e', 0 };
  STRCMP_EQUAL(a, b);
}

//--------------------------------------------------------------------------------------------------
TEST(AquaManAndBarnacleBoy_testGroup, test_PassingTest1)
{
  CHECK(0 != 5);
  CHECK("yo" != "mamma");
}

//--------------------------------------------------------------------------------------------------
TEST(AquaManAndBarnacleBoy_testGroup, test_PassingTest2)
{
  CHECK(0 != 6);
  CHECK(true);
}
