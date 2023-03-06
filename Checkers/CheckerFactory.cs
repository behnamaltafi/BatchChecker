using BatchChecker.Dto.Customer;
using BatchChecker.RequestCheckers;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BatchChecker.Checkers
{
    public interface ICheckerFactory
    {
        IRequestChecker<T> CreateChecker<T>(CheckType checkeType);
    }
    public class CheckerFactory : ICheckerFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public CheckerFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        //public IChecker CreateChecker(CheckType checkerType)
        //{

        //    Type type = Type.GetType("BatchChecker.RequestCheckers." + checkerType.ToString() + "Checker");
        //    return (IChecker)_serviceProvider.GetService(type);
        //}
        public IRequestChecker<T> CreateChecker<T>(CheckType checkeType)
        {
            Type type1 = typeof(T);
            if (checkeType == CheckType.StaffChecker)
            {
                Type type = Type.GetType($"BatchChecker.RequestCheckers.{checkeType.ToString()}");
                return (IRequestChecker<T>)_serviceProvider.GetService(type);
            }
            if (checkeType == CheckType.CustomerChecker)
            {
                Type type = Type.GetType($"BatchChecker.RequestCheckers.{checkeType.ToString()}");
                return (IRequestChecker<T>)_serviceProvider.GetService(type);
            }
            if (checkeType == CheckType.OpenAccChecker)
            {
                Type type = Type.GetType($"BatchChecker.RequestCheckers.{checkeType.ToString()}");
                return (IRequestChecker<T>)_serviceProvider.GetService(type);
            }
            if (checkeType == CheckType.SignatureChecker)
            {
                Type type = Type.GetType($"BatchChecker.RequestCheckers.{checkeType.ToString()}");
                return (IRequestChecker<T>)_serviceProvider.GetService(type);
            }
            if (checkeType == CheckType.NocrInquiryChecker)
            {
                Type type = Type.GetType($"BatchChecker.RequestCheckers.{checkeType.ToString()}");
                return (IRequestChecker<T>)_serviceProvider.GetService(type);
            }

            if (checkeType == CheckType.SamaChequeChecker)
            {
                Type type = Type.GetType($"BatchChecker.RequestCheckers.{checkeType.ToString()}");
                return (IRequestChecker<T>)_serviceProvider.GetService(type);
            }
            if (checkeType == CheckType.SamatChecker)
            {
                Type type = Type.GetType($"BatchChecker.RequestCheckers.{checkeType.ToString()}");
                return (IRequestChecker<T>)_serviceProvider.GetService(type);
            }
            if (checkeType == CheckType.SubsidyChecker)
            {
                Type type = Type.GetType($"BatchChecker.RequestCheckers.{checkeType.ToString()}");
                return (IRequestChecker<T>)_serviceProvider.GetService(type);
            }
            //if (checkeType == CheckType.FinDocCheker)
            //{
            //    Type type = Type.GetType($"BatchChecker.RequestCheckers.{checkeType.ToString()}");
            //    return (IRequestChecker<T>)_serviceProvider.GetService(type);
            //}
            if (checkeType == CheckType.PayaChecker)
            {
                Type type = Type.GetType($"BatchChecker.RequestCheckers.{checkeType.ToString()}");
                return (IRequestChecker<T>)_serviceProvider.GetService(type);
            }
            else
            {
                throw new Exception("businessType not found");
            }
        }

    }
}
