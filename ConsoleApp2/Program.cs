namespace ConsoleApp2
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var result = GetMax("aabaababbahga");
            Console.WriteLine($"Value = {result.Item1} \n i = {result.Item2} \n j = {result.Item3} \n k = {result.Item4}");
            Console.ReadKey();
        }

        public static (int, int, int, int) GetMax(string input)
        {
            int maxValue = -1;
            int first = -1;
            int second = -1;
            int third = -1;

            for (int i = 0; i < input.Length; i++)
            {
                for (int j = i; j < input.Length; j++)
                {
                    if (input[j] == input[i])
                    {
                        for (int k = i + 1; k < j; k++)
                        {
                            if (input[k] != input[i])
                            {
                                var result = i + k + j;
                                if (result > maxValue)
                                {
                                    maxValue = result;
                                    first = i;
                                    second = k;
                                    third = j;
                                }
                            }
                        }
                    }
                }
            }

            return (maxValue, first, second, third);
        }
    }
}
