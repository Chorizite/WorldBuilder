using System;

namespace Test
{
    struct MyStruct
    {
        public int Value;
        public void Mutate(int v) { Value = v; }
    }

    class Program
    {
        static void Main()
        {
            var arr = new MyStruct[1];
            arr[0].Value = 10;
            Console.WriteLine($"Before: {arr[0].Value}");
            arr[0].Mutate(20);
            Console.WriteLine($"After: {arr[0].Value}");

            if (arr[0].Value == 20)
            {
                Console.WriteLine("SUCCESS: Struct in array was mutated.");
            }
            else
            {
                Console.WriteLine("FAILURE: Struct in array was NOT mutated.");
            }
        }
    }
}
